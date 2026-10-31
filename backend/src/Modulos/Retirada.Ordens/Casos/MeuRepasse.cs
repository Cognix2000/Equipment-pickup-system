using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Retirada.Comum;
using Retirada.Ordens.Infraestrutura;

namespace Retirada.Ordens.Casos;

/// <summary>
/// Na tela do técnico: quanto tem a receber (retiradas conferidas ainda não pagas) e os últimos pagamentos que o
/// financeiro registrou. É assim que ele fica sabendo do pagamento, sem mensagem por fora.
/// </summary>
internal static class MeuRepasse
{
    internal sealed record Saldo(int Quantidade, int ValorCentavos);
    internal sealed record Pagamento(DateTimeOffset PagoEm, int ValorCentavos, int Quantidade);
    internal sealed record Resposta(int ValorPorRetiradaCentavos, Saldo AReceber, List<Pagamento> UltimosPagamentos);

    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapGet("/meu", ExecutarAsync)
            .RequireAuthorization(Politicas.Campo)
            .WithSummary("Repasse do técnico: valor a receber e últimos pagamentos");

    private static async Task<Ok<Resposta>> ExecutarAsync(ClaimsPrincipal usuario, OrdensDbContext db, ParametrosDasOrdens parametros, CancellationToken ct)
    {
        var eu = usuario.Id();
        var saldo = await db.Database.SqlQuery<Saldo>($"""
            SELECT count(*)::int AS "Quantidade", coalesce(sum(valor_centavos), 0)::int AS "ValorCentavos"
            FROM retirada.repasse_credito WHERE tecnico_id = {eu} AND pagamento_id IS NULL
            """).SingleAsync(ct);
        var pagamentos = await db.Database.SqlQuery<Pagamento>($"""
            SELECT pago_em AS "PagoEm", valor_centavos AS "ValorCentavos", quantidade AS "Quantidade"
            FROM retirada.repasse_pagamento WHERE tecnico_id = {eu} ORDER BY pago_em DESC LIMIT 5
            """).ToListAsync(ct);
        return TypedResults.Ok(new Resposta(parametros.ValorPorRetiradaCentavos, saldo, pagamentos));
    }
}
