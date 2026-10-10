using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Retirada.Acesso;
using Retirada.Banco;
using Retirada.Comum;
using Retirada.Ordens;
using Retirada.Ordens.Infraestrutura;

// Comandos de manutenção, rodados com o usuário dono do banco (ConnectionStrings:Migracao):
//   dotnet Retirada.Api.dll migrar [--dados-ficticios caminho.sql]
//   dotnet Retirada.Api.dll convidar-gestor --nome "Nome" --email gestor@empresa [--url https://endereco-do-app]
if (args.Length > 0 && args[0] is "migrar" or "convidar-gestor")
{
    var config = new ConfigurationBuilder()
        .AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
        .AddEnvironmentVariables()
        .Build();
    string? Opcao(string nome) => Array.IndexOf(args, nome) is var i && i > 0 && i + 1 < args.Length ? args[i + 1] : null;
    var conexao = Obrigatoria(config, "Migracao");

    if (args[0] == "migrar")
    {
        var dadosFicticios = Opcao("--dados-ficticios");
        await Migrador.AplicarAsync(conexao, dadosFicticios, Console.Out);
        if (dadosFicticios is not null)
        {
            // Só homologação: usuários fictícios entram com a senha de demonstração do README.
            var n = await ModuloAcesso.DefinirSenhasDeDemonstracaoAsync(conexao, "Demo@Retirada2026");
            Console.WriteLine($"Senha de demonstração definida para {n} usuário(s) fictício(s).");
        }
        return;
    }

    var nome = Opcao("--nome") ?? throw new ArgumentException("Informe --nome.");
    var email = Opcao("--email") ?? throw new ArgumentException("Informe --email.");
    var caminho = await ModuloAcesso.ConvidarPrimeiroGestorAsync(conexao, nome, email);
    Console.WriteLine($"Link de primeiro acesso (vale 48 h, uma vez só): {Opcao("--url") ?? "http://localhost:8080"}{caminho}");
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
});

// ADR-008: logs, métricas e rastros por OpenTelemetry. Só exporta se houver destino configurado
// (OTEL_EXPORTER_OTLP_ENDPOINT); sem ele, a API roda igual e nada sai da máquina.
if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService("retirada-api", serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString()))
        .WithTracing(t => t
            .AddAspNetCoreInstrumentation(o => o.Filter = c => !c.Request.Path.StartsWithSegments("/health"))
            .AddHttpClientInstrumentation()
            .AddNpgsql())
        .WithMetrics(m => m
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter("Npgsql"))
        .WithLogging()
        .UseOtlpExporter();
}

// Atrás do Nginx: IP real e HTTPS vêm dos cabeçalhos X-Forwarded-*. A API não é publicada fora da rede do Docker.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var conexaoDaApi = Obrigatoria(builder.Configuration, "Retirada");
builder.Services.AddModuloAcesso(conexaoDaApi, builder.Configuration, builder.Environment);
builder.Services.AddModuloOrdens(conexaoDaApi);
builder.Services.AddHealthChecks().AddDbContextCheck<OrdensDbContext>("banco");

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler(erro => erro.Run(async contexto =>
{
    // Regra de negócio violada vira 422 com o código da regra; parâmetro inválido, 400; o resto, 500 sem detalhe interno.
    var excecao = contexto.Features.Get<IExceptionHandlerFeature>()?.Error;
    var regra = excecao as RegraDeNegocioException;
    var requisicaoInvalida = excecao as BadHttpRequestException;
    contexto.Response.StatusCode = regra is not null ? StatusCodes.Status422UnprocessableEntity
        : requisicaoInvalida?.StatusCode ?? StatusCodes.Status500InternalServerError;
    await Results.Problem(
        title: regra?.Message ?? (requisicaoInvalida is not null
            ? "Requisição inválida."
            : "Erro inesperado. Tente de novo; se continuar, avise o suporte."),
        statusCode: contexto.Response.StatusCode,
        extensions: regra is null ? null : new Dictionary<string, object?> { ["codigo"] = regra.Codigo })
        .ExecuteAsync(contexto);
}));

// ADR-010: CSRF antes de tudo; depois sessão, limite de requisições e permissões.
// O limite vem antes da autorização para contar também quem leva 401/403.
app.UseProtecaoCsrf();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

// Contrato da API em /openapi/v1.json fora de produção (desenvolvimento e homologação).
if (!app.Environment.IsProduction())
    app.MapOpenApi().AllowAnonymous();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapModuloAcesso();
app.MapModuloOrdens();

await app.RunAsync();

static string Obrigatoria(IConfiguration config, string nome) =>
    config.GetConnectionString(nome)
    ?? throw new InvalidOperationException($"Configure ConnectionStrings:{nome} (variável ConnectionStrings__{nome}).");

/// <summary>Exposto para os testes de integração (WebApplicationFactory).</summary>
public partial class Program;
