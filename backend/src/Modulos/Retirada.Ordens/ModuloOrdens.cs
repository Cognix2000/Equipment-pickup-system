using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Retirada.Comum;
using Retirada.Ordens.Casos;
using Retirada.Ordens.Infraestrutura;

namespace Retirada.Ordens;

/// <summary>Porta de entrada do módulo: o host só chama estes dois métodos.</summary>
public static class ModuloOrdens
{
    public static IServiceCollection AddModuloOrdens(this IServiceCollection services, string connectionString, IConfiguration config)
    {
        services.AddDbContext<OrdensDbContext>(o => o.UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite()));
        // RN09: 3 visitas sem sucesso encerram a ordem. Ajustável sem mexer no código (RF18).
        services.AddSingleton(new ParametrosDasOrdens(
            LimiteDeVisitas: config.GetValue("Ordens:LimiteDeVisitas", 3),
            CarenciaHoras: config.GetValue("Ordens:CarenciaHoras", 48),          // RN03
            ModoSimulacao: config.GetValue("Importacao:ModoSimulacao", false))); // RN05: DDD 00 só com dados fictícios
        return services;
    }

    public static IEndpointRouteBuilder MapModuloOrdens(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/ordens").WithTags("Ordens").RequireAuthorization(Politicas.LerOrdens);

        ListarOrdens.Mapear(grupo);
        ObterOrdem.Mapear(grupo);

        // Comandos (ADR-009): POST /api/ordens/{id}/<ação>, todos com Idempotency-Key.
        Liberar.Mapear(grupo);
        Designar.Mapear(grupo);
        RegistrarRetirada.Mapear(grupo);
        RegistrarVisitaSemSucesso.Mapear(grupo);
        Conferir.Mapear(grupo);
        Cancelar.Mapear(grupo);
        RegistrarDevolucaoNaLoja.Mapear(grupo);
        MarcarBaixaFeita.Mapear(grupo);

        // Importação (RF03–RF05): prévia, confirmação e descarte.
        var importacoes = app.MapGroup("/api/importacoes").WithTags("Importação").RequireAuthorization(Politicas.Financeiro);
        PreverImportacao.Mapear(importacoes);
        ConfirmarImportacao.Mapear(importacoes);
        DescartarImportacao.Mapear(importacoes);

        return app;
    }
}
