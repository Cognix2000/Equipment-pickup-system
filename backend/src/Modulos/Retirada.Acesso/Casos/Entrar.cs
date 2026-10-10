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

/// <summary>RF01: e-mail e senha. Gestor e financeiro seguem para o segundo fator (RNF03).</summary>
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

        if (usuario.Exige2FA)
        {
            var etapa = usuario.Tem2FA ? EtapasDeAcesso.Codigo : EtapasDeAcesso.Configurar;
            await Sessao.AbrirAsync(http, usuario, etapa);
            return TypedResults.Ok(new RespostaDeLogin(usuario.Tem2FA ? "codigo" : "configurar", null));
        }

        usuario.RegistrarEntrada(agora);
        await db.SaveChangesAsync(ct);
        await db.AuditarAsync(usuario.Id, "login", Sessao.Ip(http), ct);
        await Sessao.AbrirAsync(http, usuario, EtapasDeAcesso.Completo);
        return TypedResults.Ok(new RespostaDeLogin("pronto", UsuarioDto.De(usuario)));
    }
}
