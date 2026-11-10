using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Retirada.Comum;
using Retirada.Ordens.Infraestrutura;

namespace Retirada.Ordens.Casos;

/// <summary>Os últimos resgates de todos os técnicos, com o comprovante: é o que o financeiro confere com o extrato do Asaas.</summary>
internal static class ResgatesDosTecnicos
{
    internal sealed record ResgateNaLista(
        Guid Id, Guid TecnicoId, string TecnicoNome, int ValorCentavos, int Quantidade, string Situacao,
        DateTimeOffset SolicitadoEm, DateTimeOffset? ConcluidoEm, string? IdFimAFim, string? ComprovanteUrl, string? MotivoFalha);

    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapGet("/", ExecutarAsync)
            .RequireAuthorization(Politicas.Financeiro)
            .WithSummary("Últimos resgates por PIX dos técnicos terceiros");

    private static async Task<Ok<List<ResgateNaLista>>> ExecutarAsync(OrdensDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await db.Database.SqlQuery<ResgateNaLista>($"""
            SELECT r.id AS "Id", r.tecnico_id AS "TecnicoId", u.nome AS "TecnicoNome", r.valor_centavos AS "ValorCentavos",
                   r.quantidade AS "Quantidade", r.situacao AS "Situacao", r.solicitado_em AS "SolicitadoEm",
                   r.concluido_em AS "ConcluidoEm", r.id_fim_a_fim AS "IdFimAFim", r.comprovante_url AS "ComprovanteUrl",
                   r.motivo_falha AS "MotivoFalha"
              FROM retirada.resgate r JOIN retirada.usuario u ON u.id = r.tecnico_id
             ORDER BY r.solicitado_em DESC LIMIT 50
            """).ToListAsync(ct));
}
