using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Retirada.Comum;
using Retirada.Ordens.Dominio;

namespace Retirada.Ordens.Casos;

/// <summary>RF13 e RN09: a ordem volta para Disponível ou, no limite de visitas, encerra sem retirada.</summary>
internal static class RegistrarVisitaSemSucesso
{
    internal sealed record PedidoDeVisita(MotivoVisitaSemSucesso Motivo, Gps Gps);

    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapPost("/{id:guid}/visita-sem-sucesso", ([AsParameters] PedidoDeComando p, PedidoDeVisita corpo, ParametrosDasOrdens parametros) =>
                ComandoDaOrdem.ExecutarAsync(p, (ordem, agora) =>
                {
                    ordem.RegistrarVisitaSemSucesso(corpo.Motivo, parametros.LimiteDeVisitas, agora, corpo.Gps.Localizacao());
                    return Task.FromResult<object?>(new { visitas = ordem.VisitasSemSucesso, limite = parametros.LimiteDeVisitas });
                }, corpo.Gps))
            .ComoComando(Politicas.Campo, "Registrar visita sem sucesso (RF13)");
}
