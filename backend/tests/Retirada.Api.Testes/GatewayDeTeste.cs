using Retirada.Ordens.Portas;

namespace Retirada.Api.Testes;

/// <summary>
/// Faz o papel do Asaas nos testes. Por padrão aceita o PIX e deixa pendente (como o Asaas real), para o teste
/// mandar o aviso pelo webhook. <see cref="Responder"/> troca a resposta do próximo pedido.
/// </summary>
internal sealed class GatewayDeTeste : IGatewayDePagamento
{
    private readonly object trava = new();
    private Func<PedidoDePix, PixNoGateway>? proxima;

    public List<PedidoDePix> Pedidos { get; } = [];
    public Dictionary<string, PixNoGateway> Consultas { get; } = [];
    public bool Ligado { get; set; } = true;

    internal void Responder(Func<PedidoDePix, PixNoGateway> resposta)
    {
        lock (trava) proxima = resposta;
    }

    Task<PixNoGateway> IGatewayDePagamento.EnviarPixAsync(PedidoDePix pedido, CancellationToken ct)
    {
        Func<PedidoDePix, PixNoGateway>? resposta;
        lock (trava)
        {
            Pedidos.Add(pedido);
            resposta = proxima;
            proxima = null;
        }
        return Task.FromResult(resposta?.Invoke(pedido) ?? new PixNoGateway($"tra_{pedido.ResgateId:N}", SituacaoNoGateway.Pendente));
    }

    Task<PixNoGateway?> IGatewayDePagamento.ConsultarAsync(string gatewayId, CancellationToken ct) =>
        Task.FromResult(Consultas.TryGetValue(gatewayId, out var pix) ? pix : null);
}
