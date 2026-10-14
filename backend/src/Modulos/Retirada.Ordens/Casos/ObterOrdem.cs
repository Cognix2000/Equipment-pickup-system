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

/// <summary>RF07: detalhe da ordem.</summary>
internal static class ObterOrdem
{
    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapGet("/{id:guid}", ExecutarAsync).WithSummary("Detalhe da ordem (RF07)");

    private static async Task<Results<Ok<OrdemDto>, NotFound>> ExecutarAsync(OrdensDbContext db, ClaimsPrincipal usuario, Guid id, CancellationToken ct)
    {
        var ordem = await Projecoes.CarregarOrdemAsync(db, id, ct);
        // RNF04: ordem de outro técnico responde como inexistente, sem revelar que existe.
        if (ordem is null || (usuario.EhTecnico() && ordem.ResponsavelId != usuario.Id())) return TypedResults.NotFound();
        return TypedResults.Ok(ordem);
    }
}
