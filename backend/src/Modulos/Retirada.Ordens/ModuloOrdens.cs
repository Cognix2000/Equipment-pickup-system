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
using Retirada.Ordens.Infraestrutura.Pagamentos;
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

        GatewayDePagamento(services, config, ambiente);
        services.AddSingleton(new ParametrosDosResgates(
            // Resgate mínimo do técnico terceiro (R$ 20,00): cada PIX tem custo no gateway.
            MinimoCentavos: ValorPositivo(config.GetValue("Pagamentos:ResgateMinimoCentavos", 2000), "Pagamentos:ResgateMinimoCentavos"),
            TokenDoWebhook: config["Pagamentos:Asaas:TokenDoWebhook"]));

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
        CadastrarOrdemManual.Mapear(grupo);
        EditarOrdem.Mapear(grupo);

        // Comandos (ADR-009): POST /api/ordens/{id}/<ação>, todos com Idempotency-Key.
        Liberar.Mapear(grupo);
        AjustarCarencia.Mapear(grupo);
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
        FolhaDoMes.Mapear(repasses);

        // Resgate por PIX do técnico terceiro (ADR-014): a tela dele, a chave PIX, o pedido e a aprovação do financeiro.
        var resgates = app.MapGroup("/api/resgates").WithTags("Resgates");
        MeuResgate.Mapear(resgates);
        CadastrarChavePix.Mapear(resgates);
        SolicitarResgate.Mapear(resgates);
        ChavesPixParaAprovar.Mapear(resgates);
        ResgatesDosTecnicos.Mapear(resgates);
        WebhooksDoAsaas.Mapear(app);

        // Localização: o técnico manda a dele; só o gestor vê a equipe. E o fundo do mapa (real ou simulado).
        var localizacao = app.MapGroup("/api/localizacao").WithTags("Localização");
        RegistrarMinhaPosicao.Mapear(localizacao);
        PosicoesDaEquipe.Mapear(localizacao);
        ConfiguracaoDoMapa.Mapear(app.MapGroup("/api/mapa").WithTags("Localização"));

        // Importação (RF03–RF05): prévia, confirmação e descarte.
        var importacoes = app.MapGroup("/api/importacoes").WithTags("Importação").RequireAuthorization(Politicas.Financeiro);
        PreverImportacao.Mapear(importacoes);
        ConfirmarImportacao.Mapear(importacoes);
        DescartarImportacao.Mapear(importacoes);

        return app;
    }

    /// <summary>
    /// Quem envia o PIX do resgate (Pagamentos:Gateway): "asaas" com a conta da Netwise, "simulado" na homologação
    /// (o PIX "cai" sem dinheiro sair) ou "desligado", o padrão. A produção recusa o simulado ao subir.
    /// </summary>
    private static void GatewayDePagamento(IServiceCollection services, IConfiguration config, IHostEnvironment ambiente)
    {
        switch (config["Pagamentos:Gateway"] ?? "desligado")
        {
            case "asaas":
                var endereco = config["Pagamentos:Asaas:Endereco"];
                var chave = config["Pagamentos:Asaas:ChaveDaApi"];
                if (string.IsNullOrWhiteSpace(endereco) || string.IsNullOrWhiteSpace(chave) || string.IsNullOrWhiteSpace(config["Pagamentos:Asaas:TokenDoWebhook"]))
                    throw new InvalidOperationException("Com Pagamentos:Gateway=asaas, configure Pagamentos:Asaas:Endereco, ChaveDaApi e TokenDoWebhook.");
                services.AddHttpClient<GatewayAsaas>(http =>
                {
                    http.BaseAddress = new Uri(endereco.TrimEnd('/') + "/");
                    http.DefaultRequestHeaders.Add("access_token", chave);
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("retirada-netwise/1.0");
                    http.Timeout = TimeSpan.FromSeconds(20);
                });
                services.AddTransient<IGatewayDePagamento>(sp => sp.GetRequiredService<GatewayAsaas>());
                break;
            case "simulado":
                if (ambiente.IsProduction())
                    throw new InvalidOperationException("Pagamentos:Gateway=simulado não vale em produção: o resgate pareceria pago sem o dinheiro sair.");
                services.AddSingleton<IGatewayDePagamento, GatewaySimulado>();
                break;
            case "desligado":
                services.AddSingleton<IGatewayDePagamento, GatewayDesligado>();
                break;
            default:
                throw new InvalidOperationException("Pagamentos:Gateway aceita asaas, simulado ou desligado.");
        }
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
