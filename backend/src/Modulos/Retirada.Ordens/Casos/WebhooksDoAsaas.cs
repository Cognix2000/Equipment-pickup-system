using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Retirada.Ordens.Dominio;
using Retirada.Ordens.Infraestrutura;
using Retirada.Ordens.Infraestrutura.Pagamentos;

namespace Retirada.Ordens.Casos;

/// <summary>
/// Os dois avisos do Asaas (ADR-014). Não têm sessão: quem prova que veio do Asaas é o token que configuramos lá,
/// no cabeçalho asaas-access-token. Sem o token configurado na API, os dois respondem 404.
/// <list type="bullet">
/// <item><b>Validação</b>, antes de soltar cada PIX: só aprovamos o que bate com um resgate em andamento, com o mesmo
/// valor. Um PIX criado por quem roubou a chave da API, ou repetido, é recusado e o Asaas cancela.</item>
/// <item><b>Eventos</b>, depois: TRANSFER_DONE traz o ID de Fim a Fim e o comprovante; FAILED e CANCELLED devolvem
/// o crédito ao técnico. Responde 200 sempre que o token confere, senão o Asaas pausa a fila de avisos.</item>
/// </list>
/// </summary>
internal static partial class WebhooksDoAsaas
{
    private sealed record ResgateAberto(Guid Id, string Situacao, int ValorCentavos, string? GatewayId, string Chave);

    public static void Mapear(IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/webhooks/asaas").WithTags("Resgates").AllowAnonymous();
        grupo.MapPost("/validacao", ValidarAsync).WithSummary("Asaas pergunta antes de soltar o PIX: aprovar ou recusar");
        grupo.MapPost("/eventos", ReceberEventoAsync).WithSummary("Asaas avisa o resultado do PIX (pago, falhou, cancelado)");
    }

    private static async Task<Results<Ok<object>, NotFound, UnauthorizedHttpResult>> ValidarAsync(
        HttpRequest requisicao, JsonElement corpo, OrdensDbContext db, ParametrosDosResgates parametros, ILoggerFactory logs, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(parametros.TokenDoWebhook)) return TypedResults.NotFound();
        if (!TokenConfere(requisicao, parametros.TokenDoWebhook)) return TypedResults.Unauthorized();
        var log = logs.CreateLogger(typeof(WebhooksDoAsaas));

        var transferencia = corpo.ValueKind == JsonValueKind.Object && corpo.TryGetProperty("transfer", out var t) ? t : default;
        if (GatewayAsaas.Texto(corpo, "type") != "TRANSFER" || transferencia.ValueKind != JsonValueKind.Object)
            return Recusar(log, "Só aprovamos transferências dos resgates do sistema.");

        var resgate = await AcharAsync(db, transferencia, ct);
        if (resgate is null) return Recusar(log, "Não há resgate do sistema para esta transferência.");
        if (resgate.Situacao is not (SituacoesDoResgate.Solicitado or SituacoesDoResgate.Enviado))
            return Recusar(log, $"O resgate está {resgate.Situacao}: não pode sair outro PIX para ele.");
        if (GatewayAsaas.Centavos(transferencia, "value") != resgate.ValorCentavos)
            return Recusar(log, "O valor não bate com o resgate.");
        // Já sabemos o id da transferência deste resgate: outra com a mesma referência não é nossa.
        if (resgate.GatewayId is not null && resgate.GatewayId != GatewayAsaas.Texto(transferencia, "id"))
            return Recusar(log, "Já existe outra transferência para este resgate.");
        var chaveDestino = transferencia.TryGetProperty("bankAccount", out var conta) ? GatewayAsaas.Texto(conta, "pixAddressKey") : null;
        if (chaveDestino is not null && !MesmaChave(chaveDestino, resgate.Chave))
            return Recusar(log, "A chave PIX de destino não é a aprovada para o técnico.");

        return TypedResults.Ok<object>(new { status = "APPROVED" });
    }

    private static async Task<Results<Ok, NotFound, UnauthorizedHttpResult>> ReceberEventoAsync(
        HttpRequest requisicao, JsonElement corpo, OrdensDbContext db, ParametrosDosResgates parametros, TimeProvider relogio,
        ILoggerFactory logs, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(parametros.TokenDoWebhook)) return TypedResults.NotFound();
        if (!TokenConfere(requisicao, parametros.TokenDoWebhook)) return TypedResults.Unauthorized();
        var log = logs.CreateLogger(typeof(WebhooksDoAsaas));

        var evento = GatewayAsaas.Texto(corpo, "event");
        if (evento is not ("TRANSFER_DONE" or "TRANSFER_FAILED" or "TRANSFER_CANCELLED")
            || !corpo.TryGetProperty("transfer", out var transferencia) || transferencia.ValueKind != JsonValueKind.Object)
            return TypedResults.Ok(); // outros avisos do Asaas não mudam nada aqui

        var resgate = await AcharAsync(db, transferencia, ct);
        if (resgate is null || (resgate.GatewayId is not null && resgate.GatewayId != GatewayAsaas.Texto(transferencia, "id")))
        {
            EventoSemResgate(log, evento, GatewayAsaas.Texto(transferencia, "id"));
            return TypedResults.Ok();
        }
        var pix = GatewayAsaas.Ler(transferencia) with
        {
            // CANCELLED às vezes chega com o status ainda PENDING: o evento é que manda.
            Situacao = evento == "TRANSFER_DONE" ? Portas.SituacaoNoGateway.Pago : Portas.SituacaoNoGateway.Falhou,
        };
        if (pix.Situacao == Portas.SituacaoNoGateway.Falhou && pix.MotivoFalha is null)
            pix = pix with { MotivoFalha = evento == "TRANSFER_CANCELLED" ? "O PIX foi cancelado." : "O banco recusou o PIX." };
        await Resgates.AplicarAsync(db, resgate.Id, pix, relogio.GetUtcNow(), log, ct);
        return TypedResults.Ok();
    }

    /// <summary>Pelo id do Asaas, ou pelo id do resgate que mandamos em externalReference (o aviso pode chegar antes de gravarmos o id).</summary>
    private static async Task<ResgateAberto?> AcharAsync(OrdensDbContext db, JsonElement transferencia, CancellationToken ct)
    {
        var gatewayId = GatewayAsaas.Texto(transferencia, "id");
        Guid? referencia = Guid.TryParse(GatewayAsaas.Texto(transferencia, "externalReference"), out var r) ? r : null;
        if (gatewayId is null && referencia is null) return null;
        return (await db.Database.SqlQuery<ResgateAberto>($"""
            SELECT r.id AS "Id", r.situacao AS "Situacao", r.valor_centavos AS "ValorCentavos", r.gateway_id AS "GatewayId", c.chave AS "Chave"
              FROM retirada.resgate r JOIN retirada.chave_pix c ON c.id = r.chave_pix_id
             WHERE r.gateway_id = {gatewayId} OR r.id = {referencia}
             ORDER BY (r.gateway_id = {gatewayId}) DESC NULLS LAST LIMIT 1
            """).ToListAsync(ct)).SingleOrDefault();
    }

    /// <summary>
    /// A mesma chave, do jeito que o Asaas devolve: sem pontuação, e o celular com ou sem o 55. E-mail e chave
    /// aleatória sem diferença de maiúsculas.
    /// </summary>
    internal static bool MesmaChave(string recebida, string aprovada)
    {
        if (string.Equals(recebida.Trim(), aprovada, StringComparison.OrdinalIgnoreCase)) return true;
        var (a, b) = (new string(recebida.Where(char.IsAsciiDigit).ToArray()), new string(aprovada.Where(char.IsAsciiDigit).ToArray()));
        if (a.Length < 11 || b.Length < 11 || aprovada.Contains('@') || aprovada.Contains('-')) return false;
        return a == b || (a.Length > b.Length ? a.EndsWith(b, StringComparison.Ordinal) : b.EndsWith(a, StringComparison.Ordinal)) && Math.Abs(a.Length - b.Length) == 2;
    }

    private static bool TokenConfere(HttpRequest requisicao, string esperado)
    {
        var recebido = requisicao.Headers["asaas-access-token"].ToString();
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(recebido), Encoding.UTF8.GetBytes(esperado));
    }

    private static Ok<object> Recusar(ILogger log, string motivo)
    {
        PixRecusado(log, motivo);
        return TypedResults.Ok<object>(new { status = "REFUSED", refuseReason = motivo });
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Validação do Asaas: PIX recusado. {Motivo}")]
    private static partial void PixRecusado(ILogger log, string motivo);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Aviso {Evento} do Asaas para a transferência {GatewayId}, que não é de nenhum resgate.")]
    private static partial void EventoSemResgate(ILogger log, string evento, string? gatewayId);
}
