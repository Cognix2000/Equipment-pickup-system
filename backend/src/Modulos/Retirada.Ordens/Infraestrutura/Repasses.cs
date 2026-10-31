using Microsoft.EntityFrameworkCore;

namespace Retirada.Ordens.Infraestrutura;

/// <summary>Gravação do crédito de repasse. Chamada dentro da transação da conferência (ComandoDaOrdem).</summary>
internal static class Repasses
{
    /// <summary>
    /// Credita a retirada conferida a quem retirou, com o valor de agora. O índice único por ordem garante
    /// um crédito só, mesmo que a mesma conferência chegue duas vezes.
    /// </summary>
    public static Task CreditarRetiradaAsync(OrdensDbContext db, Guid ordemId, Guid tecnicoId, int valorCentavos, DateTimeOffset agora, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO retirada.repasse_credito (id, tecnico_id, ordem_id, tipo, valor_centavos, creditado_em)
            VALUES ({Guid.CreateVersion7()}, {tecnicoId}, {ordemId}, 'retirada', {valorCentavos}, {agora.ToUniversalTime()})
            ON CONFLICT (ordem_id) WHERE tipo = 'retirada' DO NOTHING
            """, ct);
}
