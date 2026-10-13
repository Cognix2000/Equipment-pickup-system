using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Retirada.Comum;
using Retirada.Ordens.Dominio;

namespace Retirada.Ordens.Casos;

/// <summary>RF09: o financeiro cancela com motivo; a ordem sai da lista do técnico.</summary>
internal static class Cancelar
{
    internal sealed record PedidoDeCancelamento(MotivoCancelamento Motivo);

    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapPost("/{id:guid}/cancelar", ([AsParameters] PedidoDeComando p, PedidoDeCancelamento corpo) =>
                ComandoDaOrdem.ExecutarAsync(p, (ordem, agora) =>
                {
                    ordem.Cancelar(corpo.Motivo, agora);
                    return Task.FromResult<object?>(null);
                }))
            .ComoComando(Politicas.Financeiro, "Cancelar a ordem (RF09)");
}
