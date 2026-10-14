using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Retirada.Comum;
using Retirada.Ordens.Infraestrutura;

namespace Retirada.Ordens.Casos;

/// <summary>O financeiro desiste da prévia: ela fica registrada como descartada, e nada é gravado.</summary>
internal static class DescartarImportacao
{
    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapPost("/{id:guid}/descartar", ExecutarAsync).WithSummary("Descartar a prévia");

    private static async Task<Results<NoContent, NotFound>> ExecutarAsync(Guid id, OrdensDbContext db, ClaimsPrincipal usuario, CancellationToken ct)
    {
        var usuarioId = usuario.Id();
        var linhas = await db.Database.ExecuteSqlAsync(
            $"UPDATE retirada.importacao SET situacao = 'descartada' WHERE id = {id} AND enviado_por = {usuarioId} AND situacao = 'previa'", ct);
        return linhas == 1 ? TypedResults.NoContent() : TypedResults.NotFound();
    }
}
