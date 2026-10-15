using Retirada.Ordens.Infraestrutura;

namespace Retirada.Ordens.Rotinas;

/// <summary>RN03: quando a carência vence, a ordem Nova fica Disponível sem ninguém precisar clicar.</summary>
internal static class LiberacaoPelaCarencia
{
    /// <returns>Quantas ordens ficaram disponíveis nesta rodada.</returns>
    public static Task<int> ExecutarAsync(OrdensDbContext db, DateTimeOffset agora, CancellationToken ct) =>
        Lote.ProcessarAsync(db, $"""
            SELECT id AS "Value" FROM retirada.ordem
             WHERE status = 'nova' AND carencia_ate <= {agora}
             ORDER BY carencia_ate
             LIMIT {Lote.Tamanho}
             FOR UPDATE SKIP LOCKED
            """,
            _ => Task.FromResult<Func<Dominio.Ordem, object?>>(
                ordem => ordem.LiberarSeCarenciaVenceu(agora) ? new { carenciaAte = ordem.CarenciaAte } : null),
            ct);
}
