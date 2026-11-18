using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Retirada.Acesso.Dominio;
using Retirada.Acesso.Infraestrutura;
using Retirada.Comum;

namespace Retirada.Acesso.Casos;

internal sealed record UsuarioNaLista(
    Guid Id, string Nome, string Email, string Papel, string? Vinculo, string Situacao, bool DoisFatores, DateTimeOffset? UltimoLoginEm);

/// <summary>O link vai sem o domínio: o PWA monta com o endereço onde está aberto.</summary>
internal sealed record LinkDeConvite(string Caminho, DateTimeOffset ExpiraEm)
{
    public static LinkDeConvite De(Convite convite, string token) => new($"/primeiro-acesso#t={token}", convite.ExpiraEm);
}

/// <summary>RF02, só para o gestor: listar, cadastrar, convidar de novo, revogar, reativar e zerar o 2FA.</summary>
internal static class GestaoDeUsuarios
{
    internal sealed record PedidoDeCadastro(string Nome, string Email, string Papel, string? Vinculo);
    internal sealed record PedidoDeVinculo(string Vinculo);
    internal sealed record Cadastrado(UsuarioNaLista Usuario, LinkDeConvite Convite);

    public static void Mapear(IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/usuarios").WithTags("Usuários").RequireAuthorization(Politicas.Gestao);
        grupo.MapGet("/", ListarAsync).WithSummary("Usuários e situação (RF02)");
        grupo.MapPost("/", CadastrarAsync).WithSummary("Cadastrar usuário; ele fica pendente até usar o convite (RF02)");
        grupo.MapPost("/{id:guid}/convite", NovoConviteAsync).WithSummary("Novo link de primeiro acesso ou de nova senha");
        grupo.MapPost("/{id:guid}/revogar", (Guid id, ClaimsPrincipal s, AcessoDbContext db, TimeProvider r, HttpContext h, CancellationToken ct) =>
            MudarAsync(id, s, db, r, h, "revogar_usuario", (u, agora) => u.Revogar(s.Id(), agora), ct)).WithSummary("Revogar: corta o acesso na hora (RF02)");
        grupo.MapPost("/{id:guid}/reativar", (Guid id, ClaimsPrincipal s, AcessoDbContext db, TimeProvider r, HttpContext h, CancellationToken ct) =>
            MudarAsync(id, s, db, r, h, "reativar_usuario", (u, agora) => u.Reativar(agora), ct)).WithSummary("Reativar usuário revogado");
        grupo.MapPost("/{id:guid}/vinculo", (Guid id, PedidoDeVinculo pedido, ClaimsPrincipal s, AcessoDbContext db, TimeProvider r, HttpContext h, CancellationToken ct) =>
            MudarAsync(id, s, db, r, h, $"vinculo_usuario:{pedido.Vinculo}", (u, agora) => u.DefinirVinculo(pedido.Vinculo, agora), ct))
            .WithSummary("Mudar o vínculo do técnico: CLT (recebe na folha) ou terceiro (resgata por PIX)");
        grupo.MapPost("/{id:guid}/zerar-2fa", ZerarSegundoFatorAsync)
            .WithSummary("Zerar o 2FA de quem trocou de celular: apaga o aplicativo e as chaves de acesso");
        grupo.MapPost("/{id:guid}/apagar", ApagarAsync)
            .WithSummary("RF02 (nov/2026): depois de revogar, remove o usuário do sistema. Anonimiza e some da lista.");
    }

    private static async Task<Ok<List<UsuarioNaLista>>> ListarAsync(AcessoDbContext db, CancellationToken ct)
    {
        // Usuários apagados ficam escondidos: a linha existe só para o histórico de ordens que eles tocaram manter autor.
        var usuarios = await db.Usuarios.AsNoTracking()
            .Where(u => u.Situacao != SituacaoUsuario.Apagado)
            .OrderBy(u => u.Nome).ToListAsync(ct);
        var comChave = (await db.ChavesDeAcesso.Select(c => c.UsuarioId).Distinct().ToListAsync(ct)).ToHashSet();
        return TypedResults.Ok(usuarios.Select(u => NaLista(u, comChave.Contains(u.Id))).ToList());
    }

    /// <summary>
    /// Último passo depois de revogar: anonimiza o usuário e tira ele da lista. A linha fica no banco
    /// porque o histórico (RN12) aponta para este Id, mas sem nenhum dado pessoal e sem acesso. As chaves
    /// de acesso e os convites saem junto. Idempotente: apagar um já-apagado não dá erro, só não faz nada.
    /// </summary>
    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> ApagarAsync(
        Guid id, ClaimsPrincipal sessao, AcessoDbContext db, TimeProvider relogio, HttpContext http, CancellationToken ct)
    {
        var usuario = await db.Usuarios.SingleOrDefaultAsync(u => u.Id == id, ct);
        if (usuario is null) return TypedResults.NotFound();
        if (usuario.Situacao == SituacaoUsuario.Apagado) return TypedResults.NoContent();

        try { usuario.Apagar(sessao.Id(), relogio.GetUtcNow()); }
        catch (RegraDeNegocioException e) { return Sessao.Recusa(e.Message, e.Codigo, StatusCodes.Status422UnprocessableEntity); }

        db.ChavesDeAcesso.RemoveRange(await db.ChavesDeAcesso.Where(c => c.UsuarioId == id).ToListAsync(ct));
        db.Convites.RemoveRange(await db.Convites.Where(c => c.UsuarioId == id).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
        await db.AuditarAsync(sessao.Id(), $"apagar_usuario:{usuario.Id}", Sessao.Ip(http), ct);
        return TypedResults.NoContent();
    }

    /// <summary>Celular perdido ou trocado: some o aplicativo e todas as chaves, e as sessões abertas caem.</summary>
    private static async Task<Results<Ok<UsuarioNaLista>, NotFound>> ZerarSegundoFatorAsync(
        Guid id, ClaimsPrincipal sessao, AcessoDbContext db, TimeProvider relogio, HttpContext http, CancellationToken ct)
    {
        var usuario = await db.Usuarios.SingleOrDefaultAsync(u => u.Id == id, ct);
        if (usuario is null) return TypedResults.NotFound();

        usuario.Zerar2FA(relogio.GetUtcNow());
        db.ChavesDeAcesso.RemoveRange(await db.ChavesDeAcesso.Where(c => c.UsuarioId == id).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
        await db.AuditarAsync(sessao.Id(), $"zerar_2fa:{usuario.Id}", Sessao.Ip(http), ct);
        return TypedResults.Ok(NaLista(usuario, false));
    }

    private static async Task<Results<Created<Cadastrado>, ProblemHttpResult>> CadastrarAsync(
        PedidoDeCadastro pedido, ClaimsPrincipal sessao, AcessoDbContext db, TimeProvider relogio, HttpContext http, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var email = Usuario.NormalizarEmail(pedido.Email);
        if (await db.Usuarios.AnyAsync(u => u.Email == email, ct))
            return Sessao.Recusa("Já existe um usuário com esse e-mail.", "USUARIO_DUPLICADO", StatusCodes.Status409Conflict);

        var usuario = Usuario.Cadastrar(pedido.Nome, pedido.Email, pedido.Papel, sessao.Id(), agora, pedido.Vinculo);
        var (convite, token) = Convite.Gerar(usuario.Id, sessao.Id(), agora);
        db.Usuarios.Add(usuario);
        db.Convites.Add(convite);
        await db.SaveChangesAsync(ct);
        await db.AuditarAsync(sessao.Id(), $"cadastrar_usuario:{usuario.Id}", Sessao.Ip(http), ct);

        return TypedResults.Created($"/api/usuarios/{usuario.Id}", new Cadastrado(NaLista(usuario, false), LinkDeConvite.De(convite, token)));
    }

    private static async Task<Results<Ok<LinkDeConvite>, NotFound, ProblemHttpResult>> NovoConviteAsync(
        Guid id, ClaimsPrincipal sessao, AcessoDbContext db, TimeProvider relogio, HttpContext http, CancellationToken ct)
    {
        var usuario = await db.Usuarios.SingleOrDefaultAsync(u => u.Id == id, ct);
        if (usuario is null) return TypedResults.NotFound();
        if (usuario.Situacao == SituacaoUsuario.Revogado)
            return Sessao.Recusa("Reative o usuário antes de mandar um convite.", "ACESSO_REVOGADO", StatusCodes.Status422UnprocessableEntity);

        var (convite, token) = Convite.Gerar(usuario.Id, sessao.Id(), relogio.GetUtcNow());
        db.Convites.Add(convite);
        await db.SaveChangesAsync(ct);
        await db.AuditarAsync(sessao.Id(), $"convite_usuario:{usuario.Id}", Sessao.Ip(http), ct);
        return TypedResults.Ok(LinkDeConvite.De(convite, token));
    }

    private static async Task<Results<Ok<UsuarioNaLista>, NotFound>> MudarAsync(
        Guid id, ClaimsPrincipal sessao, AcessoDbContext db, TimeProvider relogio, HttpContext http,
        string acao, Action<Usuario, DateTimeOffset> mudanca, CancellationToken ct)
    {
        var usuario = await db.Usuarios.SingleOrDefaultAsync(u => u.Id == id, ct);
        if (usuario is null) return TypedResults.NotFound();

        mudanca(usuario, relogio.GetUtcNow());
        await db.SaveChangesAsync(ct);
        await db.AuditarAsync(sessao.Id(), $"{acao}:{usuario.Id}", Sessao.Ip(http), ct);
        return TypedResults.Ok(NaLista(usuario, await db.ChavesDeAcesso.AnyAsync(c => c.UsuarioId == id, ct)));
    }

    /// <summary>"DoisFatores": tem o aplicativo ou ao menos uma chave de acesso.</summary>
    private static UsuarioNaLista NaLista(Usuario u, bool temChave) =>
        new(u.Id, u.Nome, u.Email, u.Papel, u.Vinculo, u.Situacao.ToString().ToLowerInvariant(), u.Tem2FA || temChave, u.UltimoLoginEm);
}
