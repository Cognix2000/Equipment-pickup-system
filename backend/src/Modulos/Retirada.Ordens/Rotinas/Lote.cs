using Microsoft.EntityFrameworkCore;
using Retirada.Ordens.Dominio;
using Retirada.Ordens.Infraestrutura;

namespace Retirada.Ordens.Rotinas;

/// <summary>
/// O caminho comum das rotinas automáticas: trava um lote de ordens, aplica a regra pelo agregado e grava
/// um evento do sistema por ordem que mudou, tudo numa transação.
/// </summary>
internal static class Lote
{
    public const int Tamanho = 200;

    /// <param name="idsDoLote">SELECT dos ids (coluna "Value") com LIMIT <see cref="Tamanho"/> e FOR UPDATE SKIP LOCKED.</param>
    /// <param name="regra">Recebe os ids travados e devolve a regra de cada ordem: os dados do evento se a ordem
    /// mudou, ou nulo se a regra do domínio decidiu deixar como está.</param>
    /// <returns>Quantas ordens mudaram.</returns>
    public static async Task<int> ProcessarAsync(
        OrdensDbContext db, FormattableString idsDoLote, Func<List<Guid>, Task<Func<Ordem, object?>>> regra, CancellationToken ct)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(ct);

        // FOR UPDATE SKIP LOCKED: a ordem que alguém está mudando agora fica para a próxima rodada,
        // e duas instâncias da API dividem o lote em vez de mudar a mesma ordem duas vezes.
        // Trava só os ids e depois carrega pelo EF (FromSql não monta o endereço, que é tipo complexo);
        // as linhas seguem travadas até o fim desta transação.
        var ids = await db.Database.SqlQuery<Guid>(idsDoLote).ToListAsync(ct);
        if (ids.Count == 0) return 0;

        var aplicar = await regra(ids);
        var ordens = await db.Ordens.Where(o => ids.Contains(o.Id)).ToListAsync(ct);
        var mudadas = new List<(Ordem Ordem, StatusOrdem De, object Dados)>();
        foreach (var ordem in ordens)
        {
            var de = ordem.Status;
            if (aplicar(ordem) is { } dados) mudadas.Add((ordem, de, dados));
        }
        if (mudadas.Count == 0) return 0;

        await db.SaveChangesAsync(ct);
        foreach (var (ordem, de, dados) in mudadas)
            await RegistroDeEventos.GravarAsync(db, ordem.Id, ordem.Eventos.Single(), de, ordem.Status,
                AutorDoEvento.Sistema, Guid.CreateVersion7(), dados, ct);

        await transacao.CommitAsync(ct);
        return mudadas.Count;
    }
}
