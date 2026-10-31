using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Retirada.Comum;
using Retirada.Ordens.Dominio;
using Retirada.Ordens.Infraestrutura;

namespace Retirada.Ordens.Casos;

/// <summary>
/// O celular do técnico manda a posição enquanto o app está aberto, para o gestor acompanhar a equipe no mapa.
/// Guarda só a ÚLTIMA posição (sem trajeto): cada envio substitui o anterior. Uma posição mais velha que a guardada
/// (o celular reenviou depois de ficar sem sinal) é ignorada.
/// </summary>
internal static class RegistrarMinhaPosicao
{
    internal sealed record Pedido(double Latitude, double Longitude, double? PrecisaoM, DateTimeOffset? RegistradaEm);

    /// <summary>Relógio do celular adiantado além disso vira a hora do servidor.</summary>
    private static readonly TimeSpan ToleranciaDoRelogio = TimeSpan.FromMinutes(2);

    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapPost("/minha", ExecutarAsync)
            .RequireAuthorization(Politicas.Campo)
            .WithSummary("Técnico: posição atual do celular (só a última fica guardada)");

    private static async Task<NoContent> ExecutarAsync(
        Pedido pedido, ClaimsPrincipal usuario, OrdensDbContext db, TimeProvider relogio, CancellationToken ct)
    {
        var local = new Localizacao(pedido.Latitude, pedido.Longitude); // fora do intervalo: 422
        if (pedido.PrecisaoM is < 0)
            throw new RegraDeNegocioException("LOCALIZACAO", "Precisão inválida.");

        var agora = relogio.GetUtcNow();
        var registrada = pedido.RegistradaEm is { } r && r <= agora + ToleranciaDoRelogio ? r : agora;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO retirada.posicao_tecnico (tecnico_id, local, precisao_m, registrada_em)
            VALUES ({usuario.Id()}, ST_SetSRID(ST_MakePoint({local.Longitude}, {local.Latitude}), 4326)::geography,
                    {pedido.PrecisaoM}, {registrada.ToUniversalTime()})
            ON CONFLICT (tecnico_id) DO UPDATE
               SET local = EXCLUDED.local, precisao_m = EXCLUDED.precisao_m,
                   registrada_em = EXCLUDED.registrada_em, recebida_em = now()
             WHERE retirada.posicao_tecnico.registrada_em <= EXCLUDED.registrada_em
            """, ct);
        return TypedResults.NoContent();
    }
}
