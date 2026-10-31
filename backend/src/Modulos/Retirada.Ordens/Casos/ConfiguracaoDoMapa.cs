using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Retirada.Comum;

namespace Retirada.Ordens.Casos;

/// <summary>
/// Qual fundo o mapa usa. "real": ruas de verdade (OpenFreeMap, dados do OpenStreetMap), em produção.
/// "simulado": a cidade fictícia, na homologação, onde as ordens fictícias ficam fora de qualquer cidade real.
/// </summary>
internal static class ConfiguracaoDoMapa
{
    internal sealed record Resposta(string Estilo);

    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapGet("/", (IConfiguration config) =>
                TypedResults.Ok(new Resposta(config["Mapa:Estilo"] is "real" ? "real" : "simulado")))
            .RequireAuthorization(Politicas.LerOrdens)
            .WithSummary("Fundo do mapa: real (produção) ou simulado (homologação)");
}
