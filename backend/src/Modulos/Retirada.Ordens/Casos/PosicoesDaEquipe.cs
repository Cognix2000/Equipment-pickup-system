using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Retirada.Comum;
using Retirada.Ordens.Infraestrutura;

namespace Retirada.Ordens.Casos;

/// <summary>
/// Só o gestor acompanha onde está cada técnico ativo (estoque e financeiro não veem). Técnico sem posição aparece
/// também, para o gestor saber que o app dele está fechado ou sem permissão de localização.
/// </summary>
internal static class PosicoesDaEquipe
{
    internal sealed record TecnicoNoMapa(
        Guid Id, string Nome, double? Latitude, double? Longitude, double? PrecisaoM, DateTimeOffset? RegistradaEm, int OrdensComEle);

    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapGet("/equipe", ExecutarAsync)
            .RequireAuthorization(Politicas.Gestao)
            .WithSummary("Gestor: última posição de cada técnico ativo");

    private static async Task<Ok<List<TecnicoNoMapa>>> ExecutarAsync(OrdensDbContext db, CancellationToken ct)
    {
        var equipe = await db.Database.SqlQuery<TecnicoNoMapa>($"""
            SELECT u.id AS "Id", u.nome AS "Nome",
                   ST_Y(p.local::geometry) AS "Latitude", ST_X(p.local::geometry) AS "Longitude",
                   p.precisao_m::float8 AS "PrecisaoM", p.registrada_em AS "RegistradaEm",
                   (SELECT count(*)::int FROM retirada.ordem o
                     WHERE o.responsavel_id = u.id AND o.status IN ('atribuida', 'em_rota')) AS "OrdensComEle"
            FROM retirada.usuario u
            LEFT JOIN retirada.posicao_tecnico p ON p.tecnico_id = u.id
            WHERE u.papel = 'tecnico' AND u.situacao = 'ativo'
            ORDER BY p.registrada_em DESC NULLS LAST, u.nome
            """).ToListAsync(ct);
        return TypedResults.Ok(equipe);
    }
}
