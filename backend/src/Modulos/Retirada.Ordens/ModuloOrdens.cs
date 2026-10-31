using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Retirada.Comum;
using Retirada.Ordens.Casos;
using Retirada.Ordens.Infraestrutura;
using Retirada.Ordens.Infraestrutura.Fotos;
using Retirada.Ordens.Portas;
using Retirada.Ordens.Rotinas;

namespace Retirada.Ordens;

/// <summary>Porta de entrada do módulo: o host só chama estes dois métodos.</summary>
public static class ModuloOrdens
{
    public static IServiceCollection AddModuloOrdens(
        this IServiceCollection services, string connectionString, IConfiguration config, IHostEnvironment ambiente)
    {
        var criarArmazenamento = ArmazenamentoDeFotos(config, ambiente); // confere a configuração já na partida
        services.AddSingleton(sp => criarArmazenamento(sp));
        services.AddDbContext<OrdensDbContext>(o => o.UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite()));
        // RN09: 3 visitas sem sucesso encerram a ordem. Ajustável sem mexer no código (RF18).
        services.AddSingleton(new ParametrosDasOrdens(
            LimiteDeVisitas: config.GetValue("Ordens:LimiteDeVisitas", 3),
            CarenciaHoras: config.GetValue("Ordens:CarenciaHoras", 48),          // RN03
            ModoSimulacao: config.GetValue("Importacao:ModoSimulacao", false),   // RN05: DDD 00 só com dados fictícios
            DiasSemResposta: config.GetValue("Ordens:DiasSemResposta", 10),     // RN09
            // Repasse ao técnico por retirada conferida, em centavos (R$ 5,00). Muda só daqui para a frente.
            ValorPorRetiradaCentavos: ValorPositivo(config.GetValue("Ordens:ValorPorRetiradaCentavos", 500), "Ordens:ValorPorRetiradaCentavos")));

        // Rotinas automáticas (RN03, RN09). A cada 5 min basta: os prazos são de horas e dias. Os testes desligam para controlar o relógio.
        if (config.GetValue("Ordens:Rotinas:Ativas", true))
        {
            services.AddSingleton(new ParametrosDasRotinas(TimeSpan.FromMinutes(config.GetValue("Ordens:Rotinas:IntervaloMinutos", 5))));
            services.AddHostedService<RotinasDasOrdens>();
        }
        return services;
    }

    private static int ValorPositivo(int valor, string chave) =>
        valor > 0 ? valor : throw new InvalidOperationException($"{chave} precisa ser maior que zero.");

    public static IEndpointRouteBuilder MapModuloOrdens(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/ordens").WithTags("Ordens").RequireAuthorization(Politicas.LerOrdens);

        ListarOrdens.Mapear(grupo);
        ObterOrdem.Mapear(grupo);
        ResumoDoTecnico.Mapear(grupo);

        // Comandos (ADR-009): POST /api/ordens/{id}/<ação>, todos com Idempotency-Key.
        Liberar.Mapear(grupo);
        Designar.Mapear(grupo);
        RegistrarRetirada.Mapear(grupo);
        EnviarFoto.Mapear(grupo);
        VerFoto.Mapear(grupo);
        RegistrarVisitaSemSucesso.Mapear(grupo);
        Conferir.Mapear(grupo);
        Cancelar.Mapear(grupo);
        RegistrarDevolucaoNaLoja.Mapear(grupo);
        MarcarBaixaFeita.Mapear(grupo);
        RegistrarContato.Mapear(grupo);

        // Repasse ao técnico: o dele (técnico), o de todos e o pagamento (financeiro e gestor).
        var repasses = app.MapGroup("/api/repasses").WithTags("Repasses");
        MeuRepasse.Mapear(repasses);
        RepassesDosTecnicos.Mapear(repasses);
        RegistrarPagamentoDeRepasse.Mapear(repasses);

        // Importação (RF03–RF05): prévia, confirmação e descarte.
        var importacoes = app.MapGroup("/api/importacoes").WithTags("Importação").RequireAuthorization(Politicas.Financeiro);
        PreverImportacao.Mapear(importacoes);
        ConfirmarImportacao.Mapear(importacoes);
        DescartarImportacao.Mapear(importacoes);

        return app;
    }

    /// <summary>
    /// RN14: com Fotos:S3:Bucket configurado, as fotos vão para o S3 (SeaweedFS em homologação). Sem ele, só no
    /// desenvolvimento, ficam numa pasta. Fora do desenvolvimento, faltar a configuração impede a API de subir.
    /// </summary>
    private static Func<IServiceProvider, IArmazenamentoDeFotos> ArmazenamentoDeFotos(IConfiguration config, IHostEnvironment ambiente)
    {
        var bucket = config["Fotos:S3:Bucket"];
        if (!string.IsNullOrWhiteSpace(bucket))
        {
            var s3 = new AmazonS3Client(
                new BasicAWSCredentials(config["Fotos:S3:Chave"], config["Fotos:S3:Segredo"]),
                new AmazonS3Config
                {
                    ServiceURL = config["Fotos:S3:Endereco"],
                    AuthenticationRegion = config["Fotos:S3:Regiao"] ?? "us-east-1",
                    ForcePathStyle = true, // SeaweedFS, MinIO e a maioria dos compatíveis usam endereco/bucket/chave
                    // Só o que o S3 exige: os serviços compatíveis nem sempre aceitam as somas de verificação novas.
                    RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                    ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
                });
            return sp => new ArmazenamentoS3(s3, bucket, sp.GetRequiredService<ILogger<ArmazenamentoS3>>());
        }
        if (!ambiente.IsDevelopment())
            throw new InvalidOperationException("Configure Fotos:S3 (Endereco, Bucket, Chave e Segredo) para guardar as fotos.");
        var pasta = config["Fotos:Pasta"] ?? Path.Combine(Path.GetTempPath(), "retirada-fotos-dev");
        return _ => new ArmazenamentoEmPasta(pasta);
    }
}
