using Retirada.Ordens.Dominio;
using Retirada.Ordens.Infraestrutura;

namespace Retirada.Ordens.Rotinas;

/// <summary>
/// RN09: a ordem Disponível que passou do prazo depois da carência, com tentativa de contato e nenhuma
/// resposta, encerra sem retirada e vai para a cobrança. A regra fica no agregado; aqui só se escolhe o lote.
/// </summary>
internal static class EncerramentoSemResposta
{
    /// <returns>Quantas ordens foram encerradas nesta rodada.</returns>
    public static Task<int> ExecutarAsync(OrdensDbContext db, TimeSpan prazo, DateTimeOffset agora, CancellationToken ct) =>
        Lote.ProcessarAsync(db, $"""
            SELECT o.id AS "Value" FROM retirada.ordem o
             WHERE o.status = 'disponivel' AND o.carencia_ate <= {agora - prazo}
               AND EXISTS (SELECT 1 FROM retirada.tentativa_contato t WHERE t.ordem_id = o.id)
               AND NOT EXISTS (SELECT 1 FROM retirada.tentativa_contato t
                                WHERE t.ordem_id = o.id AND t.resultado = ANY({ContatosDaOrdem.Respostas}))
             ORDER BY o.carencia_ate
             LIMIT {Lote.Tamanho}
             FOR UPDATE OF o SKIP LOCKED
            """,
            async ids =>
            {
                // Relido depois de travar: uma resposta registrada entre a escolha do lote e agora segura a ordem.
                var contatos = await ContatosDaOrdem.ResumirAsync(db, ids, ct);
                return ordem =>
                {
                    var resumo = contatos.De(ordem.Id);
                    return ordem.EncerrarSeSemResposta(resumo, prazo, agora)
                        ? new { tentativas = resumo.Tentativas, prazoDias = prazo.TotalDays }
                        : null;
                };
            },
            ct);
}
