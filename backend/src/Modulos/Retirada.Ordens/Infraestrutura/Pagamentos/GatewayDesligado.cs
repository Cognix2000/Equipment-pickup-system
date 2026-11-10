using Retirada.Ordens.Portas;

namespace Retirada.Ordens.Infraestrutura.Pagamentos;

/// <summary>Sem conta no gateway: ninguém resgata. É o padrão da produção até a chave do Asaas ir para o .env.</summary>
internal sealed class GatewayDesligado : IGatewayDePagamento
{
    public bool Ligado => false;

    public Task<PixNoGateway> EnviarPixAsync(PedidoDePix pedido, CancellationToken ct) =>
        throw new GatewayRecusouException("O resgate por PIX ainda não está ligado.");

    public Task<PixNoGateway?> ConsultarAsync(string gatewayId, CancellationToken ct) => Task.FromResult<PixNoGateway?>(null);
}
