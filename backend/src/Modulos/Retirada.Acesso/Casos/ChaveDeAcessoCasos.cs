using System.Security.Claims;
using System.Text.Json;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Retirada.Acesso.Dominio;
using Retirada.Acesso.Infraestrutura;
using Retirada.Acesso.Seguranca;
using Retirada.Comum;

namespace Retirada.Acesso.Casos;

/// <summary>
/// ADR-012: entrar com a chave de acesso (passkey). A chave já pede a digital, o rosto ou o PIN do aparelho,
/// então vale pelos dois fatores: gestor e financeiro entram sem o aplicativo autenticador.
///
/// O WebAuthn tem dois passos: o servidor manda um desafio ("opcoes") e o aparelho devolve a resposta assinada.
/// As opções e as respostas vão no JSON padrão do WebAuthn (base64url), serializado pela própria biblioteca,
/// e não pelo JSON da API, que muda os nomes dos enums.
/// </summary>
internal static class ChaveDeAcessoCasos
{
    internal sealed record PedidoDeEntradaComChave(JsonElement Credencial);
    internal sealed record PedidoDeCadastroDeChave(JsonElement Credencial, string? Nome);
    internal sealed record ChaveNaLista(Guid Id, string Nome, DateTimeOffset CriadaEm, DateTimeOffset? UltimoUsoEm);

    /// <summary>Uma pessoa com mais que isso está cadastrando chave à toa; também segura abuso.</summary>
    public const int LimitePorUsuario = 10;

    private const string Entrada = "entrar";
    private static string Cadastro(Guid usuario) => $"cadastrar:{usuario}";

    /// <summary>Identificador do usuário dentro da chave. Não leva e-mail nem nome, que o aparelho pode mostrar a terceiros.</summary>
    public static byte[] ManipuloDoUsuario(Guid id) => id.ToByteArray();

    public static void Mapear(IEndpointRouteBuilder grupo)
    {
        grupo.MapPost("/chave/entrar/opcoes", OpcoesDeEntrada)
            .AllowAnonymous()
            .RequireRateLimiting(ModuloAcesso.LimiteDeTentativas)
            .WithSummary("Desafio para entrar com a chave de acesso (passkey)");
        grupo.MapPost("/chave/entrar", EntrarAsync)
            .AllowAnonymous()
            .RequireRateLimiting(ModuloAcesso.LimiteDeTentativas)
            .WithSummary("Entrar com a chave de acesso: vale pelos dois fatores");
        grupo.MapPost("/chave/cadastro/opcoes", OpcoesDeCadastroAsync)
            .RequireAuthorization(Politicas.LoginParcial)
            .WithSummary("Desafio para cadastrar uma chave de acesso neste aparelho");
        grupo.MapPost("/chave/cadastro", CadastrarAsync)
            .RequireAuthorization(Politicas.LoginParcial)
            .RequireRateLimiting(ModuloAcesso.LimiteDeTentativas)
            .WithSummary("Cadastrar a chave de acesso; no primeiro acesso de gestor e financeiro, conclui o login");
        grupo.MapGet("/chaves", ListarAsync).WithSummary("Chaves de acesso de quem está logado");
        grupo.MapPost("/chaves/{id:guid}/remover", RemoverAsync).WithSummary("Remover uma chave de acesso própria");
    }

    /// <summary>Sem lista de chaves: o aparelho mostra as chaves que tem para este site e a pessoa escolhe.</summary>
    private static ContentHttpResult OpcoesDeEntrada(IFido2 fido2, CerimoniaDaChave cerimonia, HttpContext http)
    {
        var opcoes = fido2.GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = [],
            UserVerification = UserVerificationRequirement.Required,
        });
        var json = opcoes.ToJson();
        cerimonia.Guardar(http, Entrada, json);
        return TypedResults.Content(json, "application/json");
    }

    private static async Task<Results<Ok<RespostaDeLogin>, ProblemHttpResult>> EntrarAsync(
        PedidoDeEntradaComChave pedido, AcessoDbContext db, IFido2 fido2, CerimoniaDaChave cerimonia,
        TimeProvider relogio, HttpContext http, CancellationToken ct)
    {
        var opcoes = cerimonia.Retirar(http, Entrada);
        if (opcoes is null) return Expirou();
        var resposta = Ler<AuthenticatorAssertionRawResponse>(pedido.Credencial);
        if (resposta?.RawId is not { Length: > 0 } credencialId) return Invalida();

        var chave = await db.ChavesDeAcesso.SingleOrDefaultAsync(c => c.CredencialId == credencialId, ct);
        if (chave is null)
            return Sessao.Recusa(
                "Esta chave de acesso não vale mais. Entre com a senha e cadastre de novo em Minha conta.", "CHAVE_DESCONHECIDA");

        var agora = relogio.GetUtcNow();
        var usuario = await db.Usuarios.SingleAsync(u => u.Id == chave.UsuarioId, ct);
        if (!usuario.PodeEntrarComChave)
        {
            await db.AuditarAsync(usuario.Id, "login_chave_recusado", Sessao.Ip(http), ct);
            return Sessao.Recusa("Seu acesso não está ativo. Fale com o gestor.", "ACESSO_INATIVO");
        }

        VerifyAssertionResult resultado;
        try
        {
            resultado = await fido2.MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = resposta,
                OriginalOptions = AssertionOptions.FromJson(opcoes),
                StoredPublicKey = chave.ChavePublica,
                StoredSignatureCounter = (uint)chave.Contador,
                IsUserHandleOwnerOfCredentialIdCallback = (p, _) =>
                    Task.FromResult(p.UserHandle.AsSpan().SequenceEqual(ManipuloDoUsuario(chave.UsuarioId))),
            }, ct);
        }
        catch (Fido2VerificationException)
        {
            await db.AuditarAsync(usuario.Id, "login_chave_falhou", Sessao.Ip(http), ct);
            return Sessao.Recusa("O aparelho não confirmou a chave de acesso. Tente de novo.", "CHAVE_RECUSADA");
        }

        chave.RegistrarUso(resultado.SignCount, agora);
        return await Concluir.EntrarAsync(usuario, db, http, agora, "login_chave", ct);
    }

    /// <summary>
    /// Cadastrar exige login completo (Minha conta) ou o primeiro acesso de gestor e financeiro, no lugar do QR code.
    /// Na etapa do código, não: quem só tem a senha não pode trocar o aplicativo por uma chave própria.
    /// </summary>
    private static async Task<Results<ContentHttpResult, ProblemHttpResult>> OpcoesDeCadastroAsync(
        ClaimsPrincipal sessao, AcessoDbContext db, IFido2 fido2, CerimoniaDaChave cerimonia, HttpContext http, CancellationToken ct)
    {
        if (!PodeCadastrar(sessao)) return Sessao.EtapaErrada();
        var usuario = await db.Usuarios.SingleAsync(u => u.Id == sessao.Id(), ct);
        var existentes = await db.ChavesDeAcesso.Where(c => c.UsuarioId == usuario.Id).Select(c => c.CredencialId).ToListAsync(ct);
        if (existentes.Count >= LimitePorUsuario) return Limite();

        var opcoes = fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User { Id = ManipuloDoUsuario(usuario.Id), Name = usuario.Email, DisplayName = usuario.Nome },
            // O aparelho recusa cadastrar de novo uma chave que já está aqui.
            ExcludeCredentials = existentes.Select(id => new PublicKeyCredentialDescriptor(id)).ToList(),
            AuthenticatorSelection = new AuthenticatorSelection
            {
                ResidentKey = ResidentKeyRequirement.Required,          // entra sem digitar o e-mail
                UserVerification = UserVerificationRequirement.Required, // digital, rosto ou PIN: o segundo fator
            },
            AttestationPreference = AttestationConveyancePreference.None, // não pede o modelo do aparelho
        });
        var json = opcoes.ToJson();
        cerimonia.Guardar(http, Cadastro(usuario.Id), json);
        return TypedResults.Content(json, "application/json");
    }

    private static async Task<Results<Ok<RespostaDeLogin>, ProblemHttpResult>> CadastrarAsync(
        PedidoDeCadastroDeChave pedido, ClaimsPrincipal sessao, AcessoDbContext db, IFido2 fido2, CerimoniaDaChave cerimonia,
        TimeProvider relogio, HttpContext http, CancellationToken ct)
    {
        if (!PodeCadastrar(sessao)) return Sessao.EtapaErrada();
        var usuario = await db.Usuarios.SingleAsync(u => u.Id == sessao.Id(), ct);
        var opcoes = cerimonia.Retirar(http, Cadastro(usuario.Id));
        if (opcoes is null) return Expirou();
        var resposta = Ler<AuthenticatorAttestationRawResponse>(pedido.Credencial);
        if (resposta is null) return Invalida();
        if (await db.ChavesDeAcesso.CountAsync(c => c.UsuarioId == usuario.Id, ct) >= LimitePorUsuario) return Limite();

        RegisteredPublicKeyCredential credencial;
        try
        {
            credencial = await fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
            {
                AttestationResponse = resposta,
                OriginalOptions = CredentialCreateOptions.FromJson(opcoes),
                IsCredentialIdUniqueToUserCallback = async (p, ct2) => !await db.ChavesDeAcesso.AnyAsync(c => c.CredencialId == p.CredentialId, ct2),
            }, ct);
        }
        catch (Fido2VerificationException)
        {
            return Sessao.Recusa("O aparelho não confirmou a chave de acesso. Tente de novo.", "CHAVE_RECUSADA", StatusCodes.Status422UnprocessableEntity);
        }

        var agora = relogio.GetUtcNow();
        db.ChavesDeAcesso.Add(ChaveDeAcesso.Cadastrar(usuario.Id, credencial.Id, credencial.PublicKey, credencial.SignCount, pedido.Nome, agora));

        // Primeiro acesso de gestor ou financeiro: a chave é o segundo fator, então o login termina aqui.
        if (Sessao.Etapa(sessao) == EtapasDeAcesso.Configurar)
            return await Concluir.EntrarAsync(usuario, db, http, agora, "chave_cadastrada", ct);

        await db.SaveChangesAsync(ct);
        await db.AuditarAsync(usuario.Id, "chave_cadastrada", Sessao.Ip(http), ct);
        return TypedResults.Ok(new RespostaDeLogin("pronto", UsuarioDto.De(usuario)));
    }

    private static async Task<Ok<List<ChaveNaLista>>> ListarAsync(ClaimsPrincipal sessao, AcessoDbContext db, CancellationToken ct)
    {
        var chaves = await db.ChavesDeAcesso.AsNoTracking()
            .Where(c => c.UsuarioId == sessao.Id())
            .OrderBy(c => c.CriadaEm)
            .Select(c => new ChaveNaLista(c.Id, c.Nome, c.CriadaEm, c.UltimoUsoEm))
            .ToListAsync(ct);
        return TypedResults.Ok(chaves);
    }

    /// <summary>Só a própria chave. Removida a última, gestor e financeiro voltam ao aplicativo ou cadastram outra no próximo login.</summary>
    private static async Task<Results<NoContent, NotFound>> RemoverAsync(
        Guid id, ClaimsPrincipal sessao, AcessoDbContext db, HttpContext http, CancellationToken ct)
    {
        var chave = await db.ChavesDeAcesso.SingleOrDefaultAsync(c => c.Id == id && c.UsuarioId == sessao.Id(), ct);
        if (chave is null) return TypedResults.NotFound();
        db.ChavesDeAcesso.Remove(chave);
        await db.SaveChangesAsync(ct);
        await db.AuditarAsync(sessao.Id(), "chave_removida", Sessao.Ip(http), ct);
        return TypedResults.NoContent();
    }

    private static bool PodeCadastrar(ClaimsPrincipal sessao) =>
        Sessao.Etapa(sessao) is EtapasDeAcesso.Completo or EtapasDeAcesso.Configurar;

    private static T? Ler<T>(JsonElement json) where T : class
    {
        try
        {
            return json.ValueKind == JsonValueKind.Object ? json.Deserialize<T>() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ProblemHttpResult Expirou() =>
        Sessao.Recusa("O tempo para usar a chave de acesso acabou. Tente de novo.", "CHAVE_EXPIRADA", StatusCodes.Status400BadRequest);

    private static ProblemHttpResult Invalida() =>
        Sessao.Recusa("O aparelho mandou uma resposta que não deu para ler. Tente de novo.", "CHAVE_INVALIDA", StatusCodes.Status400BadRequest);

    private static ProblemHttpResult Limite() =>
        Sessao.Recusa($"Você já tem {LimitePorUsuario} chaves de acesso. Remova uma que não usa mais.", "CHAVE_LIMITE", StatusCodes.Status422UnprocessableEntity);
}
