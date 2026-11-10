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
/// O técnico terceiro cadastra (ou troca) a chave PIX. Ela fica pendente até o financeiro aprovar: quem entra na
/// conta do técnico não consegue desviar o dinheiro trocando a chave. Até lá, vale a chave aprovada anterior.
/// </summary>
internal static class CadastrarChavePix
{
    internal sealed record Pedido(string Tipo, string Chave);

    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapPost("/minha-chave-pix", ExecutarAsync)
            .RequireAuthorization(Politicas.Campo)
            .WithSummary("Cadastrar ou trocar a chave PIX; vale depois que o financeiro aprova");

    private static async Task<Ok<MeuResgate.Chave>> ExecutarAsync(
        Pedido pedido, ClaimsPrincipal usuario, OrdensDbContext db, ParametrosDasOrdens regras, TimeProvider relogio, CancellationToken ct)
    {
        var eu = usuario.Id();
        if (await MeuResgate.VinculoAsync(db, eu, ct) != Vinculos.Terceiro)
            throw new RegraDeNegocioException("RESGATE_SO_TERCEIRO", "O técnico CLT recebe o repasse na folha e não precisa de chave PIX.");
        var chave = ChavePix.Validar(pedido.Tipo, pedido.Chave, regras.ModoSimulacao);
        var agora = relogio.GetUtcNow();
        var id = Guid.CreateVersion7();

        await using var transacao = await db.Database.BeginTransactionAsync(ct);
        // Uma pendente por vez: a nova toma o lugar da que ainda esperava o financeiro.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE retirada.chave_pix SET situacao = 'substituida' WHERE tecnico_id = {eu} AND situacao = 'pendente'
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO retirada.chave_pix (id, tecnico_id, tipo, chave, situacao, cadastrada_em)
            VALUES ({id}, {eu}, {chave.Tipo}, {chave.Chave}, 'pendente', {agora.ToUniversalTime()})
            """, ct);
        await transacao.CommitAsync(ct);

        return TypedResults.Ok(new MeuResgate.Chave(id, chave.Tipo, chave.Mascarada(), "pendente", agora, null));
    }
}
