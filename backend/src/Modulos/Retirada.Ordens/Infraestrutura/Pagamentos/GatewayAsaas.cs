using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Retirada.Ordens.Dominio;
using Retirada.Ordens.Portas;

namespace Retirada.Ordens.Infraestrutura.Pagamentos;

/// <summary>Configuração do Asaas (Pagamentos:Asaas:* no appsettings ou Pagamentos__Asaas__* no ambiente).</summary>
internal sealed record ConfiguracaoDoAsaas(Uri Endereco, string ChaveDaApi, string TokenDoWebhook);

/// <summary>
/// Transferência PIX do Asaas (POST /v3/transfers). A resposta chega PENDENTE: quem confirma que o dinheiro caiu
/// é o aviso TRANSFER_DONE, com o ID de Fim a Fim e o comprovante (WebhooksDoAsaas). O id do resgate vai em
/// externalReference, para o Asaas devolver nos avisos e na validação antes de pagar.
/// </summary>
internal sealed partial class GatewayAsaas(HttpClient http, ILogger<GatewayAsaas> log) : IGatewayDePagamento
{
    public bool Ligado => true;

    public async Task<PixNoGateway> EnviarPixAsync(PedidoDePix pedido, CancellationToken ct)
    {
        var corpo = new
        {
            value = pedido.ValorCentavos / 100m,
            operationType = "PIX",
            pixAddressKey = pedido.Chave.Chave,
            pixAddressKeyType = TipoNoAsaas(pedido.Chave.Tipo),
            description = pedido.Descricao,
            externalReference = pedido.ResgateId.ToString(),
        };

        HttpResponseMessage resposta;
        try
        {
            resposta = await http.PostAsJsonAsync("v3/transfers", corpo, ct);
        }
        catch (HttpRequestException erro)
        {
            ForaDoAr(erro);
            throw new GatewayRecusouException("O Asaas não respondeu. Tente de novo em alguns minutos.");
        }

        using (resposta)
        {
            var texto = await resposta.Content.ReadAsStringAsync(ct);
            if (!resposta.IsSuccessStatusCode)
            {
                var motivo = MotivoDoErro(texto) ?? $"O Asaas recusou o PIX (HTTP {(int)resposta.StatusCode}).";
                Recusado((int)resposta.StatusCode, motivo);
                throw new GatewayRecusouException(motivo);
            }
            return Ler(JsonDocument.Parse(texto).RootElement);
        }
    }

    public async Task<PixNoGateway?> ConsultarAsync(string gatewayId, CancellationToken ct)
    {
        using var resposta = await http.GetAsync($"v3/transfers/{Uri.EscapeDataString(gatewayId)}", ct);
        if (resposta.StatusCode == HttpStatusCode.NotFound) return null;
        resposta.EnsureSuccessStatusCode();
        return Ler(JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(ct)).RootElement);
    }

    /// <summary>O objeto "transfer" do Asaas, na resposta da API e nos avisos (webhooks).</summary>
    internal static PixNoGateway Ler(JsonElement transferencia)
    {
        var situacao = Texto(transferencia, "status") switch
        {
            "DONE" => SituacaoNoGateway.Pago,
            "FAILED" or "CANCELLED" => SituacaoNoGateway.Falhou,
            _ => SituacaoNoGateway.Pendente, // PENDING, SCHEDULED, BANK_PROCESSING, BLOCKED
        };
        return new PixNoGateway(
            Texto(transferencia, "id") ?? throw new GatewayRecusouException("O Asaas respondeu sem o id da transferência."),
            situacao,
            Texto(transferencia, "endToEndIdentifier"),
            Texto(transferencia, "transactionReceiptUrl"),
            situacao == SituacaoNoGateway.Falhou ? Texto(transferencia, "failReason") ?? "O banco recusou o PIX." : null);
    }

    internal static string? Texto(JsonElement objeto, string campo) =>
        objeto.ValueKind == JsonValueKind.Object && objeto.TryGetProperty(campo, out var valor) && valor.ValueKind == JsonValueKind.String
            ? valor.GetString() : null;

    /// <summary>O valor do Asaas vem em reais (25.5); aqui tudo é centavo.</summary>
    internal static int? Centavos(JsonElement objeto, string campo) =>
        objeto.ValueKind == JsonValueKind.Object && objeto.TryGetProperty(campo, out var valor) && valor.ValueKind == JsonValueKind.Number
            ? (int)Math.Round(valor.GetDecimal() * 100m, MidpointRounding.AwayFromZero) : null;

    private static string TipoNoAsaas(string tipo) => tipo switch
    {
        TiposDeChavePix.Cpf => "CPF",
        TiposDeChavePix.Cnpj => "CNPJ",
        TiposDeChavePix.Email => "EMAIL",
        TiposDeChavePix.Telefone => "PHONE",
        _ => "EVP",
    };

    /// <summary>Erro do Asaas: {"errors":[{"code":"...","description":"..."}]}. A descrição vai para o técnico.</summary>
    private static string? MotivoDoErro(string texto)
    {
        try
        {
            var raiz = JsonDocument.Parse(texto).RootElement;
            return raiz.TryGetProperty("errors", out var erros) && erros.ValueKind == JsonValueKind.Array && erros.GetArrayLength() > 0
                ? Texto(erros[0], "description") : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "O Asaas recusou o PIX do resgate: HTTP {Status}, {Motivo}")]
    private partial void Recusado(int status, string motivo);

    [LoggerMessage(Level = LogLevel.Error, Message = "O Asaas não respondeu ao pedido de PIX.")]
    private partial void ForaDoAr(Exception erro);
}
