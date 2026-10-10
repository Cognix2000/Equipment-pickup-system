using System.Security.Claims;
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

/// <summary>Quem está logado e em que etapa. O PWA chama ao abrir para decidir a tela.</summary>
internal static class QuemSouEu
{
    internal sealed record Resposta(Guid Id, string Nome, string Papel, string Etapa);

    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapGet("/eu", Executar).RequireAuthorization(Politicas.LoginParcial).WithSummary("Usuário da sessão");

    private static Ok<Resposta> Executar(ClaimsPrincipal sessao) =>
        TypedResults.Ok(new Resposta(sessao.Id(), sessao.Identity?.Name ?? string.Empty, sessao.Papel(), Sessao.Etapa(sessao)));
}

internal static class Sair
{
    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapPost("/sair", ExecutarAsync).RequireAuthorization(Politicas.LoginParcial).WithSummary("Sair");

    private static async Task<NoContent> ExecutarAsync(ClaimsPrincipal sessao, AcessoDbContext db, HttpContext http, CancellationToken ct)
    {
        await db.AuditarAsync(sessao.Id(), "logout", Sessao.Ip(http), ct);
        await Sessao.FecharAsync(http);
        return TypedResults.NoContent();
    }
}

/// <summary>RF02: primeiro acesso (ou nova senha) pelo link do convite.</summary>
internal static class AceitarConvite
{
    internal sealed record Pedido(string Token, string Senha);

    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapPost("/convite", ExecutarAsync)
            .AllowAnonymous()
            .RequireRateLimiting(ModuloAcesso.LimiteDeTentativas)
            .WithSummary("Definir a senha pelo link de convite");

    private static async Task<Results<NoContent, ProblemHttpResult>> ExecutarAsync(
        Pedido pedido, AcessoDbContext db, TimeProvider relogio, HttpContext http, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var hash = Convite.Hash(pedido.Token ?? string.Empty);
        var convite = await db.Convites.SingleOrDefaultAsync(c => c.TokenHash == hash, ct);
        if (convite is null)
            return Sessao.Recusa("Link inválido. Confira se copiou o link inteiro.", "CONVITE_INVALIDO", StatusCodes.Status400BadRequest);

        var usuario = await db.Usuarios.SingleAsync(u => u.Id == convite.UsuarioId, ct);
        HashDeSenha.ValidarPolitica(pedido.Senha ?? string.Empty, usuario.Email);
        convite.Usar(agora);
        usuario.DefinirSenha(HashDeSenha.Gerar(pedido.Senha!), agora);
        await db.SaveChangesAsync(ct);
        await db.AuditarAsync(usuario.Id, "senha_definida", Sessao.Ip(http), ct);
        return TypedResults.NoContent();
    }
}
