using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Retirada.Comum;

namespace Retirada.Ordens.Casos;

/// <summary>
/// RF15 e RN13: o cliente devolveu na loja; vale até depois de encerrada sem retirada.
/// Por enquanto encerra a ordem inteira; devolver só parte dos itens fica para depois.
/// </summary>
internal static class RegistrarDevolucaoNaLoja
{
    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapPost("/{id:guid}/devolucao-na-loja", ([AsParameters] PedidoDeComando p) =>
                ComandoDaOrdem.ExecutarAsync(p, (ordem, agora) =>
                {
                    ordem.RegistrarDevolucaoNaLoja(agora);
                    return Task.FromResult<object?>(null);
                }))
            .ComoComando(Politicas.Estoque, "Registrar devolução na loja (RF15)");
}
