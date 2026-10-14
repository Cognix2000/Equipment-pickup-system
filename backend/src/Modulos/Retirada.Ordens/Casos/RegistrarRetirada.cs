using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Retirada.Comum;

namespace Retirada.Ordens.Casos;

/// <summary>
/// RF12 e UC04: o técnico registra a série de cada item e o GPS. Série diferente da cadastrada
/// não impede: fica como divergência para o estoque. Ordem cancelada bloqueia (RN07).
/// As fotos entram com o armazenamento de evidências, numa entrega própria.
/// </summary>
internal static class RegistrarRetirada
{
    internal sealed record PedidoDeRetirada(IReadOnlyList<ItemLido> Itens, Gps Gps);

    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapPost("/{id:guid}/retirada", ([AsParameters] PedidoDeComando p, PedidoDeRetirada corpo) =>
                ComandoDaOrdem.ExecutarAsync(p, (ordem, agora) =>
                {
                    var series = ComandoDaOrdem.SeriesLidas(ordem, corpo.Itens, "RF12");
                    ordem.RegistrarRetirada(series, corpo.Gps.Localizacao(), agora);
                    return Task.FromResult<object?>(new { series = ComandoDaOrdem.ParaRegistro(series) });
                }, corpo.Gps))
            .ComoComando(Politicas.Campo, "Registrar a retirada (RF12)");
}
