using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;

namespace Retirada.Arquitetura.Testes;

/// <summary>
/// ADR-007 e ADR-009: o OpenAPI é o contrato. Qualquer mudança nele aparece neste teste
/// e precisa ser aprovada de propósito: rode com ATUALIZAR_CONTRATO=1 e versione o arquivo novo.
/// Não precisa de banco: o documento é gerado sem abrir conexão.
/// </summary>
public class ContratoDaApiTestes(WebApplicationFactory<Program> fabrica) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly string Arquivo = Path.Combine(PastaDoProjeto(), "Contrato", "openapi.v1.json");

    [Fact]
    public async Task OpenAPI_igual_ao_contrato_aprovado()
    {
        var cliente = fabrica.WithWebHostBuilder(b => b.UseEnvironment("Development")).CreateClient();
        var atual = Normalizar(await cliente.GetStringAsync("/openapi/v1.json"));

        if (Environment.GetEnvironmentVariable("ATUALIZAR_CONTRATO") == "1" || !File.Exists(Arquivo))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Arquivo)!);
            await File.WriteAllTextAsync(Arquivo, atual);
        }

        atual.ShouldBe(await File.ReadAllTextAsync(Arquivo),
            "A API mudou. Se foi de propósito, rode com ATUALIZAR_CONTRATO=1 e versione Contrato/openapi.v1.json.");
    }

    private static string Normalizar(string json)
    {
        var no = JsonNode.Parse(json)!;
        no["servers"]?.AsArray().Clear(); // o endereço do servidor de teste muda a cada execução
        return no.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n";
    }

    private static string PastaDoProjeto([System.Runtime.CompilerServices.CallerFilePath] string arquivo = "") =>
        Path.GetDirectoryName(arquivo)!;
}
