using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Retirada.Ordens.Infraestrutura;
using Retirada.Ordens.Portas;

namespace Retirada.Ordens.Rotinas;

/// <summary>
/// Resgate que ficou pelo caminho: a API caiu antes de falar com o gateway, ou o aviso do Asaas se perdeu.
/// <list type="bullet">
/// <item>"solicitado" há mais de 10 minutos: o pedido ao gateway não terminou. Vira falha e o crédito volta; se o PIX
/// chegou a ser criado, a validação do Asaas recusa, porque o resgate já não está em andamento.</item>
/// <item>"enviado" há mais de 30 minutos sem aviso: pergunta ao gateway como está e aplica a resposta.</item>
/// </list>
/// </summary>
internal static class ConciliacaoDeResgates
{
    private sealed record Parado(Guid Id, string Situacao, string? GatewayId);

    public static readonly TimeSpan PrazoSolicitado = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan PrazoEnviado = TimeSpan.FromMinutes(30);

    /// <returns>Quantos resgates mudaram nesta rodada.</returns>
    public static async Task<int> ExecutarAsync(OrdensDbContext db, IGatewayDePagamento gateway, DateTimeOffset agora, ILogger log, CancellationToken ct)
    {
        var (antesSolicitado, antesEnviado) = ((agora - PrazoSolicitado).ToUniversalTime(), (agora - PrazoEnviado).ToUniversalTime());
        var parados = await db.Database.SqlQuery<Parado>($"""
            SELECT id AS "Id", situacao AS "Situacao", gateway_id AS "GatewayId" FROM retirada.resgate
             WHERE (situacao = 'solicitado' AND solicitado_em < {antesSolicitado})
                OR (situacao = 'enviado' AND solicitado_em < {antesEnviado})
             ORDER BY solicitado_em LIMIT {Lote.Tamanho}
            """).ToListAsync(ct);

        var mudaram = 0;
        foreach (var resgate in parados)
        {
            if (resgate.Situacao == "solicitado" || resgate.GatewayId is null)
            {
                if (await Resgates.MarcarFalhaAsync(db, resgate.Id, "O pedido ao gateway não terminou. O valor voltou para o seu saldo.", agora, ct))
                    mudaram++;
                continue;
            }
            var pix = await gateway.ConsultarAsync(resgate.GatewayId, ct);
            if (pix is null)
            {
                if (await Resgates.MarcarFalhaAsync(db, resgate.Id, "O gateway não encontrou o PIX. O valor voltou para o seu saldo.", agora, ct))
                    mudaram++;
            }
            else if (pix.Situacao != SituacaoNoGateway.Pendente)
            {
                await Resgates.AplicarAsync(db, resgate.Id, pix, agora, log, ct);
                mudaram++;
            }
        }
        return mudaram;
    }
}
