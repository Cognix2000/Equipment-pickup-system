using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Retirada.Comum;

namespace Retirada.Ordens.Casos;

/// <summary>RN03: o financeiro libera a ordem antes do fim da carência de 48 h.</summary>
internal static class Liberar
{
    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapPost("/{id:guid}/liberar", ([AsParameters] PedidoDeComando p) =>
                ComandoDaOrdem.ExecutarAsync(p, (ordem, agora) =>
                {
                    ordem.Liberar(agora);
                    return Task.FromResult<object?>(null);
                }))
            .ComoComando(Politicas.Financeiro, "Liberar antes do fim da carência (RN03)");
}
