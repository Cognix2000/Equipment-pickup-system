using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Retirada.Comum;

namespace Retirada.Ordens.Casos;

/// <summary>
/// RN03 (ajustável): o financeiro muda quando a ordem sai da carência, caso a caso. O padrão continua sendo 48 h
/// a partir da rescisão (<see cref="ParametrosDasOrdens.CarenciaHoras"/>), mas o financeiro pode atrasar (aguardar
/// o cliente pagar) ou adiantar (liberar hoje).
/// </summary>
internal static class AjustarCarencia
{
    internal sealed record Pedido(DateTimeOffset LiberarEm);

    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapPost("/{id:guid}/carencia", ([AsParameters] PedidoDeComando p, Pedido pedido) =>
                ComandoDaOrdem.ExecutarAsync(p, (ordem, agora) =>
                {
                    ordem.AjustarCarencia(pedido.LiberarEm, agora);
                    return Task.FromResult<object?>(null);
                }))
            .ComoComando(Politicas.Financeiro, "Ajustar a data em que a ordem sai da carência (RN03)");
}
