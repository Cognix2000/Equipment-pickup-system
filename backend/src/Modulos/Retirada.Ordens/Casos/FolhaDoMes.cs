using System.Globalization;
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
/// O que entra na folha de cada técnico CLT no mês (horário de Brasília): as retiradas que o estoque conferiu no
/// mês e quanto ainda falta registrar como pago. O financeiro leva isso para a folha e registra o pagamento.
/// </summary>
internal static class FolhaDoMes
{
    internal sealed record TecnicoNaFolha(Guid Id, string Nome, string Situacao, int Quantidade, int ValorCentavos, int EmAbertoCentavos);
    internal sealed record Resposta(string Mes, int TotalCentavos, List<TecnicoNaFolha> Tecnicos);

    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapGet("/folha", ExecutarAsync)
            .RequireAuthorization(Politicas.Financeiro)
            .WithSummary("Repasse do mês dos técnicos CLT, para a folha (mes=AAAA-MM; sem ele, o mês atual)");

    private static async Task<Ok<Resposta>> ExecutarAsync(string? mes, OrdensDbContext db, TimeProvider relogio, CancellationToken ct)
    {
        var inicio = InicioDoMes(mes, relogio.GetUtcNow());
        var fim = inicio.AddMonths(1);
        var (de, ate) = (inicio.ToUniversalTime(), fim.ToUniversalTime());
        var tecnicos = await db.Database.SqlQuery<TecnicoNaFolha>($"""
            SELECT u.id AS "Id", u.nome AS "Nome", u.situacao AS "Situacao",
                   count(c.id)::int AS "Quantidade", coalesce(sum(c.valor_centavos), 0)::int AS "ValorCentavos",
                   coalesce(sum(c.valor_centavos) FILTER (WHERE c.pagamento_id IS NULL), 0)::int AS "EmAbertoCentavos"
              FROM retirada.usuario u
              JOIN retirada.repasse_credito c ON c.tecnico_id = u.id AND c.creditado_em >= {de} AND c.creditado_em < {ate}
             WHERE u.papel = 'tecnico' AND coalesce(u.vinculo, 'clt') = 'clt'
             GROUP BY u.id, u.nome, u.situacao
             ORDER BY u.nome
            """).ToListAsync(ct);
        return TypedResults.Ok(new Resposta(inicio.ToString("yyyy-MM", CultureInfo.InvariantCulture), tecnicos.Sum(t => t.ValorCentavos), tecnicos));
    }

    /// <summary>"2026-11" vira 01/11/2026 00:00 de Brasília; vazio é o mês de agora.</summary>
    internal static DateTimeOffset InicioDoMes(string? mes, DateTimeOffset agora)
    {
        if (string.IsNullOrWhiteSpace(mes)) return PeriodosDoResumo.De(agora).Mes;
        if (!DateTime.TryParseExact(mes, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data))
            throw new RegraDeNegocioException("FOLHA_MES", "Informe o mês no formato AAAA-MM, como 2026-11.");
        return new DateTimeOffset(data.Year, data.Month, 1, 0, 0, 0, PeriodosDoResumo.Brasilia);
    }
}
