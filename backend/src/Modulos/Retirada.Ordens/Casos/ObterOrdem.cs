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
        var linha = await db.Ordens.AsNoTracking()
            .Where(o => o.Id == id)
            .Join(db.Clientes, o => o.ClienteId, c => c.Id, (o, c) => new { Ordem = o, Cliente = c.Nome })
            .SingleOrDefaultAsync(ct);
        // RNF04: ordem de outro técnico responde como inexistente, sem revelar que existe.
        if (linha is null || (usuario.EhTecnico() && linha.Ordem.ResponsavelId != usuario.Id())) return TypedResults.NotFound();

        var equipamentos = await Projecoes.CarregarEquipamentosAsync(db, [id], ct);
        return TypedResults.Ok(OrdemDto.De(linha.Ordem, linha.Cliente, equipamentos[id]));
    }
}
