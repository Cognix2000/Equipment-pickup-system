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

/// <summary>RF06: lista ordens, com filtro por status e bairro. Consulta: lê direto do banco, sem passar pelo agregado.</summary>
internal static class ListarOrdens
{
    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapGet("/", ExecutarAsync).WithSummary("Lista ordens, com filtro por status e bairro (RF06)");

    private static async Task<Results<Ok<List<OrdemDto>>, ValidationProblem>> ExecutarAsync(
        OrdensDbContext db, ClaimsPrincipal usuario, string? status, string? bairro, CancellationToken ct)
    {
        var consulta = db.Ordens.AsNoTracking();
        if (usuario.EhTecnico())
        {
            // RNF04: técnico só vê as ordens que estão com ele, conferido aqui no servidor.
            var tecnico = usuario.Id();
            consulta = consulta.Where(o => o.ResponsavelId == tecnico);
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            // Aceita o mesmo texto que a API devolve: "atribuida", "em_rota"...
            if (!Enum.TryParse<StatusOrdem>(status.Replace("_", string.Empty), ignoreCase: true, out var filtro))
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["status"] = [$"Status desconhecido: {status}."] });
            consulta = consulta.Where(o => o.Status == filtro);
        }
        if (!string.IsNullOrWhiteSpace(bairro)) consulta = consulta.Where(o => o.Endereco.Bairro == bairro.ToUpperInvariant());

        var linhas = await consulta
            .OrderBy(o => o.CarenciaAte)
            .Join(db.Clientes, o => o.ClienteId, c => c.Id, (o, c) => new { Ordem = o, Cliente = c.Nome })
            .Take(500)
            .ToListAsync(ct);

        var equipamentos = await Projecoes.CarregarEquipamentosAsync(db, linhas.Select(l => l.Ordem.Id).ToList(), ct);
        return TypedResults.Ok(linhas.Select(l => OrdemDto.De(l.Ordem, l.Cliente, equipamentos[l.Ordem.Id])).ToList());
    }
}
