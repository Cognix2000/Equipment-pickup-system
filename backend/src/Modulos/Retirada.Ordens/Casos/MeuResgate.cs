using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Retirada.Comum;
using Retirada.Ordens.Dominio;
using Retirada.Ordens.Infraestrutura;
using Retirada.Ordens.Portas;

namespace Retirada.Ordens.Casos;

/// <summary>Regras ajustáveis do resgate (Pagamentos:* no appsettings ou Pagamentos__* no ambiente).</summary>
internal sealed record ParametrosDosResgates(int MinimoCentavos, string? TokenDoWebhook);

/// <summary>
/// A tela de resgate do técnico: o vínculo (só o terceiro resgata), o saldo que já pode sair (retiradas
/// conferidas, fora do que está a caminho), a chave PIX e os últimos resgates com o comprovante.
/// </summary>
internal static class MeuResgate
{
    internal sealed record Saldo(int Quantidade, int ValorCentavos);
    internal sealed record Chave(Guid Id, string Tipo, string Mascarada, string Situacao, DateTimeOffset CadastradaEm, string? MotivoRecusa);
    internal sealed record ResgateFeito(
        Guid Id, int ValorCentavos, int Quantidade, string Situacao, DateTimeOffset SolicitadoEm, DateTimeOffset? ConcluidoEm,
        string? IdFimAFim, string? ComprovanteUrl, string? MotivoFalha);
    internal sealed record Resposta(
        string Vinculo, bool ResgateLigado, int MinimoCentavos, Saldo Disponivel, List<Chave> Chaves, List<ResgateFeito> Resgates);

    private sealed record ChaveGravada(Guid Id, string Tipo, string Chave, string Situacao, DateTimeOffset CadastradaEm, string? MotivoRecusa);

    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapGet("/meu", ExecutarAsync)
            .RequireAuthorization(Politicas.Campo)
            .WithSummary("Resgate do técnico: vínculo, saldo que pode sair, chave PIX e últimos resgates");

    private static async Task<Ok<Resposta>> ExecutarAsync(
        ClaimsPrincipal usuario, OrdensDbContext db, ParametrosDosResgates parametros, IGatewayDePagamento gateway, CancellationToken ct) =>
        TypedResults.Ok(await LerAsync(db, usuario.Id(), parametros, gateway, ct));

    internal static async Task<Resposta> LerAsync(OrdensDbContext db, Guid eu, ParametrosDosResgates parametros, IGatewayDePagamento gateway, CancellationToken ct)
    {
        var vinculo = await VinculoAsync(db, eu, ct);
        var disponivel = await db.Database.SqlQuery<Saldo>($"""
            SELECT count(*)::int AS "Quantidade", coalesce(sum(valor_centavos), 0)::int AS "ValorCentavos"
              FROM retirada.repasse_credito WHERE tecnico_id = {eu} AND pagamento_id IS NULL AND resgate_id IS NULL
            """).SingleAsync(ct);
        // A chave em uso, a que espera o financeiro e a última recusa (se veio depois da aprovada).
        var chaves = await db.Database.SqlQuery<ChaveGravada>($"""
            SELECT id AS "Id", tipo AS "Tipo", chave AS "Chave", situacao AS "Situacao", cadastrada_em AS "CadastradaEm", motivo_recusa AS "MotivoRecusa"
              FROM retirada.chave_pix
             WHERE tecnico_id = {eu} AND situacao IN ('aprovada', 'pendente', 'recusada')
             ORDER BY cadastrada_em DESC LIMIT 3
            """).ToListAsync(ct);
        var aprovadaEm = chaves.FirstOrDefault(c => c.Situacao == "aprovada")?.CadastradaEm;
        var visiveis = chaves
            .Where(c => c.Situacao != "recusada" || aprovadaEm is null || c.CadastradaEm > aprovadaEm)
            .GroupBy(c => c.Situacao).Select(g => g.First())
            .Select(c => new Chave(c.Id, c.Tipo, new ChavePix(c.Tipo, c.Chave).Mascarada(), c.Situacao, c.CadastradaEm, c.MotivoRecusa))
            .ToList();
        var resgates = await db.Database.SqlQuery<ResgateFeito>($"""
            SELECT id AS "Id", valor_centavos AS "ValorCentavos", quantidade AS "Quantidade", situacao AS "Situacao",
                   solicitado_em AS "SolicitadoEm", concluido_em AS "ConcluidoEm", id_fim_a_fim AS "IdFimAFim",
                   comprovante_url AS "ComprovanteUrl", motivo_falha AS "MotivoFalha"
              FROM retirada.resgate WHERE tecnico_id = {eu} ORDER BY solicitado_em DESC LIMIT 10
            """).ToListAsync(ct);
        return new Resposta(vinculo, gateway.Ligado, parametros.MinimoCentavos, disponivel, visiveis, resgates);
    }

    /// <summary>Técnico sem vínculo gravado conta como CLT.</summary>
    internal static async Task<string> VinculoAsync(OrdensDbContext db, Guid tecnicoId, CancellationToken ct) =>
        (await db.Database.SqlQuery<string>($"""
            SELECT coalesce(vinculo, 'clt') AS "Value" FROM retirada.usuario WHERE id = {tecnicoId}
            """).ToListAsync(ct)).SingleOrDefault() ?? Vinculos.Clt;
}
