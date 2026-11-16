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

/// <summary>RF01: e-mail e senha. Gestor e financeiro seguem para o segundo fator (RNF03): o código do aplicativo ou a chave de acesso.</summary>
internal static class Entrar
{
    internal sealed record Pedido(string Email, string Senha);

    private const string Incorreto = "E-mail ou senha incorretos.";

    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapPost("/entrar", ExecutarAsync)
            .AllowAnonymous()
            .RequireRateLimiting(ModuloAcesso.LimiteDeTentativas)
            .WithSummary("Entrar com e-mail e senha (RF01)");

    private static async Task<Results<Ok<RespostaDeLogin>, ProblemHttpResult>> ExecutarAsync(
        Pedido pedido, AcessoDbContext db, TimeProvider relogio, HttpContext http, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var email = Usuario.NormalizarEmail(pedido.Email);
        var usuario = await db.Usuarios.SingleOrDefaultAsync(u => u.Email == email, ct);

        if (usuario is null)
        {
            HashDeSenha.Verificar(pedido.Senha ?? string.Empty, null); // mesmo tempo de resposta: não revela se o e-mail existe
            return Sessao.Recusa(Incorreto, "LOGIN_INCORRETO");
        }

        if (usuario.EstaBloqueado(agora))
        {
            await db.AuditarAsync(usuario.Id, "login_bloqueado", Sessao.Ip(http), ct);
            return Sessao.Recusa("Muitas tentativas erradas. Tente de novo em 15 minutos.", "ACESSO_BLOQUEADO", StatusCodes.Status429TooManyRequests);
        }

        var senhaConfere = HashDeSenha.Verificar(pedido.Senha ?? string.Empty, usuario.SenhaHash);
        if (!senhaConfere || !usuario.PodeEntrar(agora))
        {
            if (usuario.Situacao == SituacaoUsuario.Ativo && !senhaConfere)
            {
                usuario.RegistrarFalha(agora);
                await db.SaveChangesAsync(ct);
            }
            await db.AuditarAsync(usuario.Id, "login_falhou", Sessao.Ip(http), ct);
            return Sessao.Recusa(Incorreto, "LOGIN_INCORRETO");
        }

        // RNF03 (nov/2026): 2FA é opcional. Quem tem TOTP ou chave de acesso usa como segundo fator;
        // quem não tem, entra direto com a senha. Sem obrigatoriedade por papel.
        if (usuario.Tem2FA)
        {
            await Sessao.AbrirAsync(http, usuario, EtapasDeAcesso.Codigo);
            return TypedResults.Ok(new RespostaDeLogin("codigo", null));
        }

        // Sem TOTP mas com chave de acesso cadastrada: confirma com ela, sem abrir sessão (a chave fecha o login sozinha).
        if (await db.ChavesDeAcesso.AnyAsync(c => c.UsuarioId == usuario.Id, ct))
            return TypedResults.Ok(new RespostaDeLogin("chave", null));

        // Sem nenhum segundo fator: entra direto com a senha.
        usuario.RegistrarEntrada(agora);
        await db.SaveChangesAsync(ct);
        await db.AuditarAsync(usuario.Id, "login", Sessao.Ip(http), ct);
        await Sessao.AbrirAsync(http, usuario, EtapasDeAcesso.Completo);
        return TypedResults.Ok(new RespostaDeLogin("pronto", UsuarioDto.De(usuario)));
    }
}
