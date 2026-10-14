using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Retirada.Comum;
using Retirada.Ordens.Dominio;

namespace Retirada.Ordens.Casos;

/// <summary>
/// RF14, RN08 e UC06: o estoque lê a série de cada item. Tudo certo, a ordem vira Conferida;
/// com divergência (ou item faltando), continua Concluída e os itens divergentes voltam marcados.
/// </summary>
internal static class Conferir
{
    internal sealed record PedidoDeConferencia(IReadOnlyList<ItemLido> Itens);

    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapPost("/{id:guid}/conferencia", ([AsParameters] PedidoDeComando p, PedidoDeConferencia corpo) =>
                ComandoDaOrdem.ExecutarAsync(p, (ordem, agora) =>
                {
                    var series = ComandoDaOrdem.SeriesLidas(ordem, corpo.Itens, "RF14");
                    var resultado = ordem.Conferir(series, p.Usuario.Id(), agora);
                    return Task.FromResult<object?>(new
                    {
                        series = ComandoDaOrdem.ParaRegistro(series),
                        divergentes = resultado.Divergentes.Select(e => e.Id),
                    });
                }))
            .ComoComando(Politicas.Estoque, "Conferir no estoque (RF14)");
}
