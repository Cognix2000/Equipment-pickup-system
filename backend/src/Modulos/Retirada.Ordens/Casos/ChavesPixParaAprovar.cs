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
/// O financeiro (ou o gestor) confere com o técnico e aprova a chave PIX antes de o dinheiro ir para ela.
/// A chave aparece inteira só aqui. Aprovar uma chave nova aposenta a anterior.
/// </summary>
internal static class ChavesPixParaAprovar
{
    internal sealed record ChaveParaAprovar(
        Guid Id, Guid TecnicoId, string TecnicoNome, string Tipo, string Chave, DateTimeOffset CadastradaEm, bool TrocaDeChave);
    internal sealed record PedidoDeRecusa(string? Motivo);
    private sealed record Pendente(Guid Id, Guid TecnicoId);

    public static void Mapear(IEndpointRouteBuilder grupo)
    {
        grupo.MapGet("/chaves-pix", ListarAsync).RequireAuthorization(Politicas.Financeiro)
            .WithSummary("Chaves PIX esperando aprovação do financeiro");
        grupo.MapPost("/chaves-pix/{id:guid}/aprovar", AprovarAsync).RequireAuthorization(Politicas.Financeiro)
            .WithSummary("Aprovar a chave PIX: os próximos resgates vão para ela");
        grupo.MapPost("/chaves-pix/{id:guid}/recusar", RecusarAsync).RequireAuthorization(Politicas.Financeiro)
            .WithSummary("Recusar a chave PIX; o técnico vê o motivo e cadastra outra");
    }

    private static async Task<Ok<List<ChaveParaAprovar>>> ListarAsync(OrdensDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await db.Database.SqlQuery<ChaveParaAprovar>($"""
            SELECT c.id AS "Id", c.tecnico_id AS "TecnicoId", u.nome AS "TecnicoNome", c.tipo AS "Tipo", c.chave AS "Chave",
                   c.cadastrada_em AS "CadastradaEm",
                   EXISTS (SELECT 1 FROM retirada.chave_pix a WHERE a.tecnico_id = c.tecnico_id AND a.situacao = 'aprovada') AS "TrocaDeChave"
              FROM retirada.chave_pix c JOIN retirada.usuario u ON u.id = c.tecnico_id
             WHERE c.situacao = 'pendente'
             ORDER BY c.cadastrada_em
            """).ToListAsync(ct));

    private static async Task<Results<NoContent, NotFound>> AprovarAsync(
        Guid id, ClaimsPrincipal usuario, OrdensDbContext db, TimeProvider relogio, CancellationToken ct)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(ct);
        var chave = await TravarPendenteAsync(db, id, ct);
        if (chave is null) return TypedResults.NotFound();
        var agora = relogio.GetUtcNow().ToUniversalTime();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE retirada.chave_pix SET situacao = 'substituida' WHERE tecnico_id = {chave.TecnicoId} AND situacao = 'aprovada'
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE retirada.chave_pix SET situacao = 'aprovada', decidida_em = {agora}, decidida_por = {usuario.Id()} WHERE id = {id}
            """, ct);
        await transacao.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> RecusarAsync(
        Guid id, PedidoDeRecusa pedido, ClaimsPrincipal usuario, OrdensDbContext db, TimeProvider relogio, CancellationToken ct)
    {
        var motivo = string.IsNullOrWhiteSpace(pedido.Motivo) ? null : pedido.Motivo.Trim();
        if (motivo is { Length: > 200 })
            throw new RegraDeNegocioException("PIX_MOTIVO", "O motivo passa de 200 caracteres.");
        await using var transacao = await db.Database.BeginTransactionAsync(ct);
        if (await TravarPendenteAsync(db, id, ct) is null) return TypedResults.NotFound();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE retirada.chave_pix SET situacao = 'recusada', decidida_em = {relogio.GetUtcNow().ToUniversalTime()},
                   decidida_por = {usuario.Id()}, motivo_recusa = {motivo}
             WHERE id = {id}
            """, ct);
        await transacao.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Pendente?> TravarPendenteAsync(OrdensDbContext db, Guid id, CancellationToken ct) =>
        (await db.Database.SqlQuery<Pendente>($"""
            SELECT id AS "Id", tecnico_id AS "TecnicoId" FROM retirada.chave_pix WHERE id = {id} AND situacao = 'pendente' FOR UPDATE
            """).ToListAsync(ct)).SingleOrDefault();
}
