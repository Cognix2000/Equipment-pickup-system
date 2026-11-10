using Retirada.Ordens.Dominio;

namespace Retirada.Ordens.Portas;

/// <summary>O PIX que o sistema pede ao gateway. O id do resgate vai junto, para o gateway devolver nos avisos.</summary>
internal sealed record PedidoDePix(Guid ResgateId, int ValorCentavos, ChavePix Chave, string Descricao);

internal enum SituacaoNoGateway { Pendente, Pago, Falhou }

/// <summary>O que o gateway sabe do PIX. O ID de Fim a Fim só vem quando o dinheiro cai.</summary>
internal sealed record PixNoGateway(string GatewayId, SituacaoNoGateway Situacao, string? IdFimAFim = null, string? ComprovanteUrl = null, string? MotivoFalha = null);

/// <summary>O gateway recusou o pedido (chave inválida, sem saldo, fora do ar): o resgate falha e o crédito volta.</summary>
internal sealed class GatewayRecusouException(string motivo) : Exception(motivo);

/// <summary>
/// Quem envia o PIX do resgate (ADR-014): o Asaas em produção, um simulado na homologação e nos testes, ou
/// desligado quando a conta ainda não existe. Trocar de gateway é escrever outro adaptador desta porta.
/// </summary>
internal interface IGatewayDePagamento
{
    /// <summary>Falso quando não há gateway configurado: o botão de resgate some e a API recusa.</summary>
    bool Ligado { get; }

    Task<PixNoGateway> EnviarPixAsync(PedidoDePix pedido, CancellationToken ct);

    /// <returns>A situação atual, ou nulo se o gateway não conhece esse id.</returns>
    Task<PixNoGateway?> ConsultarAsync(string gatewayId, CancellationToken ct);
}
