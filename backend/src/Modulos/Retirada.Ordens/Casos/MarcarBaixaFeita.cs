using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Retirada.Comum;

namespace Retirada.Ordens.Casos;

/// <summary>RF19: o financeiro marca que deu a baixa no HubSoft; a ordem sai da lista "Para dar baixa".</summary>
internal static class MarcarBaixaFeita
{
    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapPost("/{id:guid}/baixa-hubsoft", ([AsParameters] PedidoDeComando p) =>
                ComandoDaOrdem.ExecutarAsync(p, (ordem, agora) =>
                {
                    ordem.MarcarBaixaFeita(p.Usuario.Id(), agora);
                    return Task.FromResult<object?>(null);
                }))
            .ComoComando(Politicas.Financeiro, "Marcar baixa feita no HubSoft (RF19)");
}
