using Microsoft.EntityFrameworkCore;
using Retirada.Ordens.Dominio;

namespace Retirada.Ordens.Infraestrutura;

/// <summary>Resumo das tentativas de contato (RN10) de várias ordens numa consulta só.</summary>
internal static class ContatosDaOrdem
{
    /// <summary>Os resultados que contam como resposta, tirados da regra do domínio para não repetir a lista no SQL.</summary>
    public static readonly string[] Respostas =
        [.. Enum.GetValues<ResultadoContato>().Where(Ordem.ClienteRespondeu).Select(r => OrdensDbContext.ParaSnake(r.ToString()))];

    public static async Task<Dictionary<Guid, ResumoDosContatos>> ResumirAsync(OrdensDbContext db, IReadOnlyCollection<Guid> ordens, CancellationToken ct)
    {
        if (ordens.Count == 0) return [];
        var ids = ordens.ToArray();
        var linhas = await db.Database.SqlQuery<Linha>($"""
            SELECT ordem_id AS "OrdemId", count(*)::int AS "Tentativas", bool_or(resultado = ANY({Respostas})) AS "ClienteRespondeu"
              FROM retirada.tentativa_contato
             WHERE ordem_id = ANY({ids})
             GROUP BY ordem_id
            """).ToListAsync(ct);
        return linhas.ToDictionary(l => l.OrdemId, l => new ResumoDosContatos(l.Tentativas, l.ClienteRespondeu));
    }

    public static ResumoDosContatos De(this Dictionary<Guid, ResumoDosContatos> resumos, Guid ordem) =>
        resumos.GetValueOrDefault(ordem, ResumoDosContatos.Nenhum);

    internal sealed record Linha(Guid OrdemId, int Tentativas, bool ClienteRespondeu);
}
