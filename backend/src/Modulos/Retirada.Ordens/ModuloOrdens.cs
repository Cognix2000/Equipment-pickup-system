using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Retirada.Comum;
using Retirada.Ordens.Casos;
using Retirada.Ordens.Infraestrutura;
using Retirada.Ordens.Rotinas;

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
            ModoSimulacao: config.GetValue("Importacao:ModoSimulacao", false),   // RN05: DDD 00 só com dados fictícios
            DiasSemResposta: config.GetValue("Ordens:DiasSemResposta", 10)));    // RN09

        // Rotinas automáticas (RN03, RN09). A cada 5 min basta: os prazos são de horas e dias. Os testes desligam para controlar o relógio.
        if (config.GetValue("Ordens:Rotinas:Ativas", true))
        {
            services.AddSingleton(new ParametrosDasRotinas(TimeSpan.FromMinutes(config.GetValue("Ordens:Rotinas:IntervaloMinutos", 5))));
            services.AddHostedService<RotinasDasOrdens>();
        }
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
        RegistrarContato.Mapear(grupo);

        // Importação (RF03–RF05): prévia, confirmação e descarte.
        var importacoes = app.MapGroup("/api/importacoes").WithTags("Importação").RequireAuthorization(Politicas.Financeiro);
        PreverImportacao.Mapear(importacoes);
        ConfirmarImportacao.Mapear(importacoes);
        DescartarImportacao.Mapear(importacoes);

        return app;
    }
}
