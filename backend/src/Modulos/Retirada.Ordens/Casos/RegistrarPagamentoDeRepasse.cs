using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Retirada.Comum;
using Retirada.Ordens.Dominio;
using Retirada.Ordens.Infraestrutura;

namespace Retirada.Ordens.Casos;

/// <summary>
/// O financeiro registra que pagou o técnico: tudo o que estava em aberto vira um pagamento só, com data e valor.
/// O técnico vê o pagamento na tela dele. Clique duplo não paga duas vezes: o segundo já não acha saldo.
/// </summary>
internal static class RegistrarPagamentoDeRepasse
{
    internal sealed record Pedido(int ValorCentavos, string? Observacao);
    internal sealed record Registrado(Guid Id, DateTimeOffset PagoEm, int ValorCentavos, int Quantidade);
    private sealed record CreditoAberto(Guid Id, int ValorCentavos);

    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapPost("/{tecnicoId:guid}/pagamentos", ExecutarAsync)
            .RequireAuthorization(Politicas.Financeiro)
            .WithSummary("Registrar o pagamento de tudo o que o técnico tem a receber");

    private static async Task<Results<Ok<Registrado>, NotFound>> ExecutarAsync(
        Guid tecnicoId, Pedido pedido, ClaimsPrincipal usuario, OrdensDbContext db, TimeProvider relogio, CancellationToken ct)
    {
        var existe = await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT 1 FROM retirada.usuario WHERE id = {tecnicoId} AND papel = 'tecnico') AS "Value"
            """).SingleAsync(ct);
        if (!existe) return TypedResults.NotFound();

        await using var transacao = await db.Database.BeginTransactionAsync(ct);
        // Trava os créditos em aberto: outro pagamento ao mesmo tempo espera e, depois, não acha saldo.
        // O que está num resgate a caminho fica de fora: o PIX já vai pagar.
        var abertos = await db.Database.SqlQuery<CreditoAberto>($"""
            SELECT id AS "Id", valor_centavos AS "ValorCentavos" FROM retirada.repasse_credito
            WHERE tecnico_id = {tecnicoId} AND pagamento_id IS NULL AND resgate_id IS NULL FOR UPDATE
            """).ToListAsync(ct);

        var observacao = string.IsNullOrWhiteSpace(pedido.Observacao) ? null : pedido.Observacao.Trim();
        PagamentoDeRepasse.Validar(pedido.ValorCentavos, abertos.Sum(c => c.ValorCentavos), abertos.Count, observacao);

        var pagamento = new Registrado(Guid.CreateVersion7(), relogio.GetUtcNow(), abertos.Sum(c => c.ValorCentavos), abertos.Count);
        var ids = abertos.Select(c => c.Id).ToArray();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO retirada.repasse_pagamento (id, tecnico_id, valor_centavos, quantidade, observacao, pago_em, registrado_por)
            VALUES ({pagamento.Id}, {tecnicoId}, {pagamento.ValorCentavos}, {pagamento.Quantidade}, {observacao}, {pagamento.PagoEm.ToUniversalTime()}, {usuario.Id()})
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE retirada.repasse_credito SET pagamento_id = {pagamento.Id} WHERE id = ANY({ids})
            """, ct);
        await transacao.CommitAsync(ct);

        return TypedResults.Ok(pagamento);
    }
}
