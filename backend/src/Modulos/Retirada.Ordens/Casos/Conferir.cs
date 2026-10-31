using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Retirada.Comum;
using Retirada.Ordens.Dominio;
using Retirada.Ordens.Infraestrutura;

namespace Retirada.Ordens.Casos;

/// <summary>
/// RF14, RN08 e UC06: o estoque lê a série de cada item. Tudo certo, a ordem vira Conferida;
/// com divergência (ou item faltando), continua Concluída e os itens divergentes voltam marcados.
/// Conferida, a retirada gera o crédito de repasse do técnico que retirou.
/// </summary>
internal static class Conferir
{
    internal sealed record PedidoDeConferencia(IReadOnlyList<ItemLido> Itens);

    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapPost("/{id:guid}/conferencia", ([AsParameters] PedidoDeComando p, PedidoDeConferencia corpo, ParametrosDasOrdens parametros) =>
                ComandoDaOrdem.ExecutarAsync(p, (ordem, agora) =>
                {
                    var series = ComandoDaOrdem.SeriesLidas(ordem, corpo.Itens, "RF14");
                    var resultado = ordem.Conferir(series, p.Usuario.Id(), agora);
                    return Task.FromResult<object?>(new
                    {
                        series = ComandoDaOrdem.ParaRegistro(series),
                        divergentes = resultado.Divergentes.Select(e => e.Id),
                    });
                },
                // Repasse: conferida sem divergência, a retirada vira crédito de quem retirou, na mesma transação.
                gravarJunto: (ordem, agora) => ordem.Status == StatusOrdem.Conferida && ordem.ResponsavelId is Guid tecnico
                    ? Repasses.CreditarRetiradaAsync(p.Db, ordem.Id, tecnico, parametros.ValorPorRetiradaCentavos, agora, p.Ct)
                    : Task.CompletedTask))
            .ComoComando(Politicas.Estoque, "Conferir no estoque (RF14)");
}
