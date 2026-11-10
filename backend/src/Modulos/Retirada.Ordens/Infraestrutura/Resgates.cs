using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Retirada.Ordens.Dominio;
using Retirada.Ordens.Portas;

namespace Retirada.Ordens.Infraestrutura;

/// <summary>
/// O que muda o resgate depois que ele sai: a resposta do gateway, o aviso (webhook) e a conciliação da rotina
/// passam todos por aqui. Cada passo trava o resgate e só anda para a frente: chegar duas vezes não paga duas vezes.
/// </summary>
internal static partial class Resgates
{
    private sealed record Travado(Guid Id, Guid TecnicoId, string Situacao, int ValorCentavos, int Quantidade);

    /// <summary>O gateway aceitou e ainda não pagou: guarda o id dele para os avisos e a consulta.</summary>
    public static async Task MarcarEnviadoAsync(OrdensDbContext db, Guid resgateId, string gatewayId, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE retirada.resgate SET situacao = 'enviado', gateway_id = {gatewayId}
             WHERE id = {resgateId} AND situacao = 'solicitado'
            """, ct);

    /// <summary>
    /// O PIX caiu: o resgate vira pagamento do repasse, com o ID de Fim a Fim e o comprovante, e os créditos
    /// reservados ficam pagos. O técnico vê na tela dele.
    /// </summary>
    /// <returns>Falso quando o resgate já estava concluído (aviso repetido) ou não existe.</returns>
    public static async Task<bool> ConcluirPagoAsync(OrdensDbContext db, Guid resgateId, PixNoGateway pix, DateTimeOffset agora, ILogger log, CancellationToken ct)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(ct);
        var resgate = await TravarAsync(db, resgateId, ct);
        if (resgate is null) return false;
        if (resgate.Situacao == SituacoesDoResgate.Falhou)
        {
            // Só acontece se a validação antes de pagar estiver desligada no Asaas: o crédito pode já ter ido em outro resgate.
            PagoDepoisDeFalhar(log, resgateId, pix.GatewayId);
            return false;
        }
        if (resgate.Situacao == SituacoesDoResgate.Pago) return false;

        var pagamentoId = Guid.CreateVersion7();
        var quando = agora.ToUniversalTime();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO retirada.repasse_pagamento (id, tecnico_id, valor_centavos, quantidade, observacao, pago_em, registrado_por)
            VALUES ({pagamentoId}, {resgate.TecnicoId}, {resgate.ValorCentavos}, {resgate.Quantidade}, 'Resgate por PIX', {quando}, {resgate.TecnicoId})
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE retirada.repasse_credito SET pagamento_id = {pagamentoId} WHERE resgate_id = {resgateId} AND pagamento_id IS NULL
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE retirada.resgate
               SET situacao = 'pago', gateway_id = coalesce(gateway_id, {pix.GatewayId}), id_fim_a_fim = {Limitar(pix.IdFimAFim, 64)},
                   comprovante_url = {Limitar(pix.ComprovanteUrl, 500)}, concluido_em = {quando}, pagamento_id = {pagamentoId}
             WHERE id = {resgateId}
            """, ct);
        await transacao.CommitAsync(ct);
        return true;
    }

    /// <summary>O PIX não saiu: o resgate fica como falhou, com o motivo, e os créditos voltam ao saldo.</summary>
    /// <returns>Falso quando o resgate já estava concluído ou não existe.</returns>
    public static async Task<bool> MarcarFalhaAsync(OrdensDbContext db, Guid resgateId, string motivo, DateTimeOffset agora, CancellationToken ct)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(ct);
        var resgate = await TravarAsync(db, resgateId, ct);
        if (resgate is null || resgate.Situacao is SituacoesDoResgate.Pago or SituacoesDoResgate.Falhou) return false;

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE retirada.resgate SET situacao = 'falhou', motivo_falha = {Limitar(motivo, 300)}, concluido_em = {agora.ToUniversalTime()}
             WHERE id = {resgateId}
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE retirada.repasse_credito SET resgate_id = NULL WHERE resgate_id = {resgateId} AND pagamento_id IS NULL
            """, ct);
        await transacao.CommitAsync(ct);
        return true;
    }

    /// <summary>Aplica o que o gateway diz do PIX (resposta, aviso ou consulta).</summary>
    public static async Task AplicarAsync(OrdensDbContext db, Guid resgateId, PixNoGateway pix, DateTimeOffset agora, ILogger log, CancellationToken ct)
    {
        switch (pix.Situacao)
        {
            case SituacaoNoGateway.Pago:
                await ConcluirPagoAsync(db, resgateId, pix, agora, log, ct);
                break;
            case SituacaoNoGateway.Falhou:
                await MarcarFalhaAsync(db, resgateId, pix.MotivoFalha ?? "O banco recusou o PIX.", agora, ct);
                break;
            default:
                await MarcarEnviadoAsync(db, resgateId, pix.GatewayId, ct);
                break;
        }
    }

    private static async Task<Travado?> TravarAsync(OrdensDbContext db, Guid resgateId, CancellationToken ct) =>
        (await db.Database.SqlQuery<Travado>($"""
            SELECT id AS "Id", tecnico_id AS "TecnicoId", situacao AS "Situacao", valor_centavos AS "ValorCentavos", quantidade AS "Quantidade"
              FROM retirada.resgate WHERE id = {resgateId} FOR UPDATE
            """).ToListAsync(ct)).SingleOrDefault();

    private static string? Limitar(string? texto, int tamanho) =>
        texto is null ? null : texto.Length <= tamanho ? texto : texto[..tamanho];

    [LoggerMessage(Level = LogLevel.Critical,
        Message = "O gateway confirmou o PIX {GatewayId} de um resgate dado como falho ({ResgateId}). Confira no Asaas: o técnico pode ter recebido duas vezes.")]
    private static partial void PagoDepoisDeFalhar(ILogger log, Guid resgateId, string gatewayId);
}
