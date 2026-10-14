using Microsoft.EntityFrameworkCore;
using Retirada.Ordens.Dominio;
using Retirada.Ordens.Infraestrutura;

namespace Retirada.Ordens.Rotinas;

/// <summary>
/// RN03: quando a carência vence, a ordem Nova fica Disponível sem ninguém precisar clicar.
/// Cada rodada pega um lote, muda pelo agregado e grava um evento do sistema por ordem, tudo numa transação.
/// </summary>
internal static class LiberacaoPelaCarencia
{
    public const int Lote = 200;

    /// <returns>Quantas ordens ficaram disponíveis nesta rodada.</returns>
    public static async Task<int> ExecutarAsync(OrdensDbContext db, DateTimeOffset agora, CancellationToken ct)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(ct);

        // FOR UPDATE SKIP LOCKED: a ordem que alguém está mudando agora fica para a próxima rodada,
        // e duas instâncias da API dividem o lote em vez de liberar a mesma ordem duas vezes.
        // Trava só os ids e depois carrega pelo EF (FromSql não monta o endereço, que é tipo complexo);
        // as linhas seguem travadas até o fim desta transação.
        var ids = await db.Database
            .SqlQuery<Guid>($"""
                SELECT id AS "Value" FROM retirada.ordem
                 WHERE status = 'nova' AND carencia_ate <= {agora}
                 ORDER BY carencia_ate
                 LIMIT {Lote}
                 FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);
        if (ids.Count == 0) return 0;

        var vencidas = await db.Ordens.Where(o => ids.Contains(o.Id)).ToListAsync(ct);

        var liberadas = vencidas.Where(o => o.LiberarSeCarenciaVenceu(agora)).ToList();
        if (liberadas.Count == 0) return 0;

        await db.SaveChangesAsync(ct);
        foreach (var ordem in liberadas)
            await RegistroDeEventos.GravarAsync(db, ordem.Id, ordem.Eventos.Single(), StatusOrdem.Nova, ordem.Status,
                AutorDoEvento.Sistema, Guid.CreateVersion7(), new { carenciaAte = ordem.CarenciaAte }, ct);

        await transacao.CommitAsync(ct);
        return liberadas.Count;
    }
}
