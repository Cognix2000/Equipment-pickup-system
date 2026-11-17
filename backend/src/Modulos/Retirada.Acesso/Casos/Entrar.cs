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

/// <summary>RF01: e-mail e senha. Com TOTP ativado pede o código em seguida (RNF03);
/// a chave de acesso NÃO é segundo fator: é um caminho alternativo de entrada (ADR-015, nov/2026).</summary>
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

        // RNF03 (nov/2026): 2FA é opcional. Quem tem TOTP ativado confirma com o código do aplicativo.
        // A chave de acesso NÃO é tratada aqui: ela é um caminho de entrada próprio em /chave/entrar (ADR-015).
        // Em desktop sem biometria, isso deixaria o usuário preso — por isso a chave fica opcional, escolhida pelo usuário.
        if (usuario.Tem2FA)
        {
            await Sessao.AbrirAsync(http, usuario, EtapasDeAcesso.Codigo);
            return TypedResults.Ok(new RespostaDeLogin("codigo", null));
        }

        // Sem TOTP: entra direto com a senha (mesmo que o usuário tenha chave de acesso cadastrada).
        usuario.RegistrarEntrada(agora);
        await db.SaveChangesAsync(ct);
        await db.AuditarAsync(usuario.Id, "login", Sessao.Ip(http), ct);
        await Sessao.AbrirAsync(http, usuario, EtapasDeAcesso.Completo);
        return TypedResults.Ok(new RespostaDeLogin("pronto", UsuarioDto.De(usuario)));
    }
}
