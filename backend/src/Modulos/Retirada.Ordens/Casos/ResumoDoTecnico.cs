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
/// Controle do próprio técnico, na tela dele: quanto já recolheu, quanto o estoque já conferiu e o que falta.
/// Conta a partir do histórico (evento_ordem), então é a mesma conta que o painel e, depois, o valor a receber vão usar.
/// "Conferidas" são as retiradas DELE que o estoque conferiu no período: é o que vale para o valor a receber.
/// </summary>
internal static class ResumoDoTecnico
{
    internal sealed record Contagem(int Retiradas, int Conferidas, int VisitasSemSucesso);
    internal sealed record Resposta(int Pendentes, int AguardandoConferencia, Contagem Hoje, Contagem Semana, Contagem Mes);

    private sealed record Linha(
        int Pendentes, int Aguardando,
        int RetiradasHoje, int RetiradasSemana, int RetiradasMes,
        int ConferidasHoje, int ConferidasSemana, int ConferidasMes,
        int VisitasHoje, int VisitasSemana, int VisitasMes);

    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapGet("/meu-resumo", ExecutarAsync)
            .RequireAuthorization(Politicas.Campo)
            .WithSummary("Resumo do técnico: retiradas, conferidas e visitas de hoje, da semana e do mês");

    private static async Task<Ok<Resposta>> ExecutarAsync(ClaimsPrincipal usuario, OrdensDbContext db, TimeProvider relogio, CancellationToken ct)
    {
        var eu = usuario.Id();
        var p = PeriodosDoResumo.De(relogio.GetUtcNow());
        // O Npgsql só grava timestamptz em UTC: os inícios saem de Brasília e vão convertidos.
        var (inicio, hoje, semana, mes) = (p.Inicio.ToUniversalTime(), p.Hoje.ToUniversalTime(), p.Semana.ToUniversalTime(), p.Mes.ToUniversalTime());

        // Uma ida ao banco. As retiradas e visitas são as que ELE registrou (ator_id); as conferidas são as
        // conferências do estoque em ordens que ele retirou. O período conta pela hora do evento (ocorrido_em).
        var l = await db.Database.SqlQuery<Linha>($"""
            WITH minhas AS (
                SELECT DISTINCT ordem_id FROM retirada.evento_ordem WHERE tipo = 'retirada' AND ator_id = {eu}
            ),
            eventos AS (
                SELECT e.tipo, e.ocorrido_em,
                       CASE WHEN e.tipo = 'retirada' AND e.ator_id = {eu} THEN 'r'
                            WHEN e.tipo = 'visita_sem_sucesso' AND e.ator_id = {eu} THEN 'v'
                            WHEN e.tipo = 'conferida' AND e.ordem_id IN (SELECT ordem_id FROM minhas) THEN 'c' END AS tipo_resumo
                FROM retirada.evento_ordem e
                WHERE e.ocorrido_em >= {inicio}
            )
            SELECT
                (SELECT count(*)::int FROM retirada.ordem WHERE responsavel_id = {eu} AND status IN ('atribuida', 'em_rota')) AS "Pendentes",
                (SELECT count(*)::int FROM retirada.ordem o JOIN minhas m ON m.ordem_id = o.id WHERE o.status = 'concluida') AS "Aguardando",
                count(*) FILTER (WHERE tipo_resumo = 'r' AND ocorrido_em >= {hoje})::int   AS "RetiradasHoje",
                count(*) FILTER (WHERE tipo_resumo = 'r' AND ocorrido_em >= {semana})::int AS "RetiradasSemana",
                count(*) FILTER (WHERE tipo_resumo = 'r' AND ocorrido_em >= {mes})::int    AS "RetiradasMes",
                count(*) FILTER (WHERE tipo_resumo = 'c' AND ocorrido_em >= {hoje})::int   AS "ConferidasHoje",
                count(*) FILTER (WHERE tipo_resumo = 'c' AND ocorrido_em >= {semana})::int AS "ConferidasSemana",
                count(*) FILTER (WHERE tipo_resumo = 'c' AND ocorrido_em >= {mes})::int    AS "ConferidasMes",
                count(*) FILTER (WHERE tipo_resumo = 'v' AND ocorrido_em >= {hoje})::int   AS "VisitasHoje",
                count(*) FILTER (WHERE tipo_resumo = 'v' AND ocorrido_em >= {semana})::int AS "VisitasSemana",
                count(*) FILTER (WHERE tipo_resumo = 'v' AND ocorrido_em >= {mes})::int    AS "VisitasMes"
            FROM eventos
            """).SingleAsync(ct);

        return TypedResults.Ok(new Resposta(
            l.Pendentes, l.Aguardando,
            new Contagem(l.RetiradasHoje, l.ConferidasHoje, l.VisitasHoje),
            new Contagem(l.RetiradasSemana, l.ConferidasSemana, l.VisitasSemana),
            new Contagem(l.RetiradasMes, l.ConferidasMes, l.VisitasMes)));
    }
}
