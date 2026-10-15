using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Retirada.Comum;
using Retirada.Ordens.Infraestrutura;
using Retirada.Ordens.Portas;

namespace Retirada.Ordens.Casos;

/// <summary>
/// RN14: a foto de uma retirada registrada. O bucket não é público: a API confere a permissão
/// (técnico só da ordem dele, RNF04) e entrega o arquivo.
/// </summary>
internal static class VerFoto
{
    internal sealed record ArquivoDaFoto(string Chave, Guid? Responsavel);

    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapGet("/{id:guid}/fotos/{fotoId:guid}", ExecutarAsync)
            .WithSummary("Foto de uma retirada registrada (RN14)")
            .Produces(StatusCodes.Status200OK, contentType: "image/jpeg");

    private static async Task<Results<FileStreamHttpResult, NotFound>> ExecutarAsync(
        Guid id, Guid fotoId, OrdensDbContext db, IArmazenamentoDeFotos armazenamento, ClaimsPrincipal usuario, HttpContext http, CancellationToken ct)
    {
        var arquivo = await db.Database.SqlQuery<ArquivoDaFoto>($"""
            SELECT e.arquivo_chave AS "Chave", o.responsavel_id AS "Responsavel"
              FROM retirada.evidencia e JOIN retirada.ordem o ON o.id = e.ordem_id
             WHERE e.id = {fotoId} AND e.ordem_id = {id}
            """).SingleOrDefaultAsync(ct);
        if (arquivo is null || (usuario.EhTecnico() && arquivo.Responsavel != usuario.Id())) return TypedResults.NotFound();

        var conteudo = await armazenamento.AbrirAsync(arquivo.Chave, ct);
        if (conteudo is null) return TypedResults.NotFound();

        // A foto não muda (evidencia é só acréscimo); o navegador pode guardar, mas só para quem viu.
        http.Response.Headers.CacheControl = "private, max-age=86400, immutable";
        return TypedResults.Stream(conteudo, "image/jpeg");
    }
}
