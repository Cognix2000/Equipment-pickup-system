using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Retirada.Comum;
using Retirada.Ordens.Infraestrutura;

namespace Retirada.Ordens.Casos;

/// <summary>
/// Para o financeiro e o gestor: quanto cada técnico tem a receber e, no detalhe, quais retiradas compõem o valor.
/// A retirada aparece pelo nº da OS e pelo bairro, sem o nome do cliente: para pagar o técnico, isso basta.
/// </summary>
internal static class RepassesDosTecnicos
{
    internal sealed record TecnicoNaLista(Guid Id, string Nome, string Situacao, string Vinculo, int Quantidade, int ValorCentavos, DateTimeOffset? UltimoPagamentoEm);
    internal sealed record Credito(Guid Id, DateTimeOffset CreditadoEm, int ValorCentavos, int? NumeroOs, string? Bairro);
    internal sealed record Pagamento(Guid Id, DateTimeOffset PagoEm, int ValorCentavos, int Quantidade, string? Observacao);
    internal sealed record Detalhe(Guid Id, string Nome, int Quantidade, int ValorCentavos, List<Credito> EmAberto, List<Pagamento> Pagamentos);

    public static void Mapear(IEndpointRouteBuilder grupo)
    {
        grupo.MapGet("/", ListarAsync).RequireAuthorization(Politicas.RepasseLeitura).WithSummary("Repasse de cada técnico: retiradas conferidas a pagar");
        grupo.MapGet("/{tecnicoId:guid}", DetalharAsync).RequireAuthorization(Politicas.RepasseLeitura).WithSummary("Retiradas a pagar e pagamentos de um técnico");
    }

    /// <summary>Técnico revogado sem nada a receber sai da lista; com saldo, continua até ser pago.</summary>
    private static async Task<Ok<List<TecnicoNaLista>>> ListarAsync(OrdensDbContext db, CancellationToken ct)
    {
        var lista = await db.Database.SqlQuery<TecnicoNaLista>($"""
            SELECT u.id AS "Id", u.nome AS "Nome", u.situacao AS "Situacao", coalesce(u.vinculo, 'clt') AS "Vinculo",
                   count(c.id)::int AS "Quantidade", coalesce(sum(c.valor_centavos), 0)::int AS "ValorCentavos",
                   (SELECT max(p.pago_em) FROM retirada.repasse_pagamento p WHERE p.tecnico_id = u.id) AS "UltimoPagamentoEm"
            FROM retirada.usuario u
            LEFT JOIN retirada.repasse_credito c ON c.tecnico_id = u.id AND c.pagamento_id IS NULL AND c.resgate_id IS NULL
            WHERE u.papel = 'tecnico'
            GROUP BY u.id, u.nome, u.situacao, u.vinculo
            HAVING u.situacao = 'ativo' OR count(c.id) > 0
            ORDER BY coalesce(sum(c.valor_centavos), 0) DESC, u.nome
            """).ToListAsync(ct);
        return TypedResults.Ok(lista);
    }

    private static async Task<Results<Ok<Detalhe>, NotFound>> DetalharAsync(Guid tecnicoId, OrdensDbContext db, CancellationToken ct)
    {
        var nome = await db.Database.SqlQuery<string>($"""
            SELECT nome AS "Value" FROM retirada.usuario WHERE id = {tecnicoId} AND papel = 'tecnico'
            """).SingleOrDefaultAsync(ct);
        if (nome is null) return TypedResults.NotFound();

        var emAberto = await db.Database.SqlQuery<Credito>($"""
            SELECT c.id AS "Id", c.creditado_em AS "CreditadoEm", c.valor_centavos AS "ValorCentavos", o.numero_os AS "NumeroOs", o.bairro AS "Bairro"
            FROM retirada.repasse_credito c LEFT JOIN retirada.ordem o ON o.id = c.ordem_id
            WHERE c.tecnico_id = {tecnicoId} AND c.pagamento_id IS NULL AND c.resgate_id IS NULL
            ORDER BY c.creditado_em DESC
            """).ToListAsync(ct);
        var pagamentos = await db.Database.SqlQuery<Pagamento>($"""
            SELECT id AS "Id", pago_em AS "PagoEm", valor_centavos AS "ValorCentavos", quantidade AS "Quantidade", observacao AS "Observacao"
            FROM retirada.repasse_pagamento WHERE tecnico_id = {tecnicoId} ORDER BY pago_em DESC LIMIT 12
            """).ToListAsync(ct);

        return TypedResults.Ok(new Detalhe(tecnicoId, nome, emAberto.Count, emAberto.Sum(c => c.ValorCentavos), emAberto, pagamentos));
    }
}
