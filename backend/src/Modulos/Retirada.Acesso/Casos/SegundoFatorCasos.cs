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

internal sealed record PedidoDeCodigo(string Codigo);

/// <summary>RNF03: confere o código de 6 dígitos de quem já tem o aplicativo configurado.</summary>
internal static class ConfirmarCodigo
{
    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapPost("/codigo", ExecutarAsync)
            .RequireAuthorization(Politicas.LoginParcial)
            .RequireRateLimiting(ModuloAcesso.LimiteDeTentativas)
            .WithSummary("Confirmar o código do aplicativo autenticador");

    private static async Task<Results<Ok<RespostaDeLogin>, ProblemHttpResult>> ExecutarAsync(
        PedidoDeCodigo pedido, ClaimsPrincipal sessao, AcessoDbContext db, SegundoFator segundoFator,
        TimeProvider relogio, HttpContext http, CancellationToken ct)
    {
        if (Sessao.Etapa(sessao) != EtapasDeAcesso.Codigo) return Sessao.EtapaErrada();
        var agora = relogio.GetUtcNow();
        var usuario = await db.Usuarios.SingleAsync(u => u.Id == sessao.Id(), ct);

        var passo = usuario.TotpSegredoCifrado is null ? null : segundoFator.Verificar(usuario.TotpSegredoCifrado, pedido.Codigo, agora);
        if (passo is null || !usuario.AceitarPassoTotp(passo.Value, agora))
        {
            usuario.RegistrarFalha(agora);
            await db.SaveChangesAsync(ct);
            await db.AuditarAsync(usuario.Id, "login_2fa_falhou", Sessao.Ip(http), ct);
            if (usuario.EstaBloqueado(agora)) await Sessao.FecharAsync(http);
            return Sessao.Recusa("Código incorreto ou já usado. Confira o aplicativo e tente de novo.", "CODIGO_INCORRETO");
        }

        return await Concluir.EntrarAsync(usuario, db, http, agora, "login", ct);
    }
}

/// <summary>Primeiro login de gestor ou financeiro: gera o segredo para o QR code.</summary>
internal static class IniciarConfiguracao2FA
{
    internal sealed record Resposta(string EnderecoOtpAuth, string Segredo);

    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapPost("/2fa/iniciar", ExecutarAsync)
            .RequireAuthorization(Politicas.LoginParcial)
            .WithSummary("Gerar o QR code para cadastrar o aplicativo autenticador");

    private static async Task<Results<Ok<Resposta>, ProblemHttpResult>> ExecutarAsync(
        ClaimsPrincipal sessao, AcessoDbContext db, SegundoFator segundoFator, TimeProvider relogio, CancellationToken ct)
    {
        if (Sessao.Etapa(sessao) != EtapasDeAcesso.Configurar) return Sessao.EtapaErrada();
        var usuario = await db.Usuarios.SingleAsync(u => u.Id == sessao.Id(), ct);

        var (base32, cifrado) = segundoFator.NovoSegredo();
        usuario.PrepararTotp(cifrado, relogio.GetUtcNow());
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new Resposta(SegundoFator.EnderecoOtpAuth(usuario.Email, base32), base32));
    }
}

/// <summary>Confirma o aplicativo com o primeiro código e conclui o login.</summary>
internal static class ConfirmarConfiguracao2FA
{
    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapPost("/2fa/confirmar", ExecutarAsync)
            .RequireAuthorization(Politicas.LoginParcial)
            .RequireRateLimiting(ModuloAcesso.LimiteDeTentativas)
            .WithSummary("Ativar o 2FA com o primeiro código do aplicativo");

    private static async Task<Results<Ok<RespostaDeLogin>, ProblemHttpResult>> ExecutarAsync(
        PedidoDeCodigo pedido, ClaimsPrincipal sessao, AcessoDbContext db, SegundoFator segundoFator,
        TimeProvider relogio, HttpContext http, CancellationToken ct)
    {
        if (Sessao.Etapa(sessao) != EtapasDeAcesso.Configurar) return Sessao.EtapaErrada();
        var agora = relogio.GetUtcNow();
        var usuario = await db.Usuarios.SingleAsync(u => u.Id == sessao.Id(), ct);

        if (usuario.TotpSegredoCifrado is null)
            return Sessao.Recusa("Gere o QR code antes de confirmar.", "2FA_SEM_SEGREDO", StatusCodes.Status409Conflict);

        var passo = segundoFator.Verificar(usuario.TotpSegredoCifrado, pedido.Codigo, agora);
        if (passo is null)
            return Sessao.Recusa("Código incorreto. Confira se o relógio do celular está certo.", "CODIGO_INCORRETO");

        usuario.AtivarTotp(passo.Value, agora);
        return await Concluir.EntrarAsync(usuario, db, http, agora, "2fa_ativado", ct);
    }
}

internal static class Concluir
{
    public static async Task<Results<Ok<RespostaDeLogin>, ProblemHttpResult>> EntrarAsync(
        Usuario usuario, AcessoDbContext db, HttpContext http, DateTimeOffset agora, string acao, CancellationToken ct)
    {
        usuario.RegistrarEntrada(agora);
        await db.SaveChangesAsync(ct);
        await db.AuditarAsync(usuario.Id, acao, Sessao.Ip(http), ct);
        await Sessao.AbrirAsync(http, usuario, EtapasDeAcesso.Completo);
        return TypedResults.Ok(new RespostaDeLogin("pronto", UsuarioDto.De(usuario)));
    }
}
