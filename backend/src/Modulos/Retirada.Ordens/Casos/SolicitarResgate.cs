using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Retirada.Comum;
using Retirada.Ordens.Dominio;
using Retirada.Ordens.Infraestrutura;
using Retirada.Ordens.Portas;

namespace Retirada.Ordens.Casos;

/// <summary>
/// O técnico terceiro aperta "Resgatar": todo o saldo das retiradas conferidas vai por PIX para a chave aprovada.
/// Primeiro o resgate é gravado e os créditos ficam reservados (nada sai sem registro); depois vem o pedido ao
/// gateway, fora da transação. Se o gateway recusar, os créditos voltam na hora; se a API cair no meio, a rotina
/// de conciliação desfaz, e a validação do Asaas recusa o PIX que não bate com um resgate em andamento.
/// </summary>
internal static partial class SolicitarResgate
{
    private sealed record ChaveAprovada(Guid Id, string Tipo, string Chave);
    private sealed record CreditoLivre(Guid Id, int ValorCentavos);

    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapPost("/", ExecutarAsync)
            .RequireAuthorization(Politicas.Campo)
            .WithSummary("Resgatar por PIX o saldo das retiradas conferidas (só técnico terceiro)");

    private static async Task<Ok<MeuResgate.Resposta>> ExecutarAsync(
        ClaimsPrincipal usuario, OrdensDbContext db, IGatewayDePagamento gateway, ParametrosDosResgates parametros,
        TimeProvider relogio, ILoggerFactory logs, CancellationToken ct)
    {
        var eu = usuario.Id();
        if (!gateway.Ligado)
            throw new RegraDeNegocioException("RESGATE_INDISPONIVEL", "O resgate por PIX ainda não está ligado. Fale com o financeiro.");

        var agora = relogio.GetUtcNow();
        var resgateId = Guid.CreateVersion7();
        ChaveAprovada chave;
        int valor;
        await using (var transacao = await db.Database.BeginTransactionAsync(ct))
        {
            var vinculo = await MeuResgate.VinculoAsync(db, eu, ct);
            chave = (await db.Database.SqlQuery<ChaveAprovada>($"""
                SELECT id AS "Id", tipo AS "Tipo", chave AS "Chave" FROM retirada.chave_pix WHERE tecnico_id = {eu} AND situacao = 'aprovada'
                """).ToListAsync(ct)).SingleOrDefault()!;
            var emAndamento = await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS (SELECT 1 FROM retirada.resgate WHERE tecnico_id = {eu} AND situacao IN ('solicitado', 'enviado')) AS "Value"
                """).SingleAsync(ct);
            // Trava os créditos livres: dois cliques ao mesmo tempo esperam um pelo outro e o segundo não acha saldo.
            var livres = await db.Database.SqlQuery<CreditoLivre>($"""
                SELECT id AS "Id", valor_centavos AS "ValorCentavos" FROM retirada.repasse_credito
                 WHERE tecnico_id = {eu} AND pagamento_id IS NULL AND resgate_id IS NULL FOR UPDATE
                """).ToListAsync(ct);
            valor = livres.Sum(c => c.ValorCentavos);
            RegrasDoResgate.ConferirSePodeResgatar(vinculo, chave is not null, emAndamento, valor, parametros.MinimoCentavos);

            try
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO retirada.resgate (id, tecnico_id, chave_pix_id, valor_centavos, quantidade, situacao, solicitado_em)
                    VALUES ({resgateId}, {eu}, {chave!.Id}, {valor}, {livres.Count}, 'solicitado', {agora.ToUniversalTime()})
                    """, ct);
            }
            catch (PostgresException erro) when (erro.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                throw new RegraDeNegocioException("RESGATE_EM_ANDAMENTO", "Já tem um resgate a caminho. Espere ele cair para pedir outro.");
            }
            var ids = livres.Select(c => c.Id).ToArray();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE retirada.repasse_credito SET resgate_id = {resgateId} WHERE id = ANY({ids})
                """, ct);
            await transacao.CommitAsync(ct);
        }

        var log = logs.CreateLogger(typeof(SolicitarResgate));
        try
        {
            var pix = await gateway.EnviarPixAsync(
                new PedidoDePix(resgateId, valor, new ChavePix(chave.Tipo, chave.Chave), "Repasse das retiradas - Netwise"), ct);
            await Resgates.AplicarAsync(db, resgateId, pix, relogio.GetUtcNow(), log, ct);
        }
        catch (GatewayRecusouException erro)
        {
            await Resgates.MarcarFalhaAsync(db, resgateId, erro.Message, relogio.GetUtcNow(), ct);
        }
        catch (Exception erro) when (erro is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Sem resposta (tempo esgotado): não dá para saber se o PIX saiu. O resgate fica "solicitado"; o aviso
            // do gateway ou a conciliação da rotina decidem, e o crédito não entra em outro resgate até lá.
            SemResposta(log, erro, resgateId);
        }

        return TypedResults.Ok(await MeuResgate.LerAsync(db, eu, parametros, gateway, ct));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "O gateway não respondeu ao PIX do resgate {ResgateId}; fica para o aviso ou a conciliação.")]
    private static partial void SemResposta(ILogger log, Exception erro, Guid resgateId);
}
