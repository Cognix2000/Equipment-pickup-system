using System.Security.Cryptography;
using Retirada.Ordens.Portas;

namespace Retirada.Ordens.Infraestrutura.Pagamentos;

/// <summary>
/// Homologação: o PIX "cai" na hora, com um ID de Fim a Fim inventado (ISPB 00000000, que não existe), e nenhum
/// dinheiro sai. A API recusa subir com ele em produção.
/// </summary>
internal sealed class GatewaySimulado(TimeProvider relogio) : IGatewayDePagamento
{
    public bool Ligado => true;

    public Task<PixNoGateway> EnviarPixAsync(PedidoDePix pedido, CancellationToken ct)
    {
        var sufixo = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant()[..11];
        var idFimAFim = $"E00000000{relogio.GetUtcNow():yyyyMMddHHmm}{sufixo}";
        return Task.FromResult(new PixNoGateway($"simulado-{pedido.ResgateId:N}", SituacaoNoGateway.Pago, idFimAFim));
    }

    public Task<PixNoGateway?> ConsultarAsync(string gatewayId, CancellationToken ct) => Task.FromResult<PixNoGateway?>(null);
}
