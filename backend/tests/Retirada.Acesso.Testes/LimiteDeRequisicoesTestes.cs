using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Retirada.Comum;
using Shouldly;

namespace Retirada.Acesso.Testes;

/// <summary>Limite geral da API: por usuário com sessão, por IP sem sessão.</summary>
[Collection(ColecaoDaApi.Nome)]
public class LimiteDeRequisicoesTestes(ApiDeTeste api)
{
    // Cada teste sobe a API com limites baixos e contadores zerados.
    private WebApplicationFactory<Program> ApiComLimite(int porUsuario, int anonimo) =>
        api.WithWebHostBuilder(b => b
            .UseSetting("Acesso:RequisicoesPorMinuto", porUsuario.ToString())
            .UseSetting("Acesso:RequisicoesAnonimasPorMinuto", anonimo.ToString()));

    private static HttpClient Cliente(WebApplicationFactory<Program> fabrica)
    {
        var cliente = fabrica.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        cliente.DefaultRequestHeaders.Add("X-Retirada", "1");
        return cliente;
    }

    private async Task<HttpClient> TecnicoLogadoAsync(WebApplicationFactory<Program> fabrica)
    {
        var tecnico = await api.CriarUsuarioAsync(Papeis.Tecnico);
        var cliente = Cliente(fabrica);
        var resposta = await cliente.PostAsJsonAsync("/api/acesso/entrar", new { email = tecnico.Email, senha = ApiDeTeste.Senha });
        resposta.StatusCode.ShouldBe(HttpStatusCode.OK);
        return cliente;
    }

    [FatoComBanco]
    public async Task Usuario_que_passa_do_limite_recebe_429_e_os_outros_seguem_normais()
    {
        await using var fabrica = ApiComLimite(porUsuario: 5, anonimo: 10);
        var primeiro = await TecnicoLogadoAsync(fabrica);
        var segundo = await TecnicoLogadoAsync(fabrica);

        for (var i = 0; i < 5; i++) (await primeiro.GetAsync("/api/ordens")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var barrada = await primeiro.GetAsync("/api/ordens");

        barrada.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        barrada.Headers.RetryAfter.ShouldNotBeNull();
        (await barrada.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("codigo").GetString().ShouldBe("MUITAS_TENTATIVAS");
        (await segundo.GetAsync("/api/ordens")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [FatoComBanco]
    public async Task Sem_sessao_o_limite_e_por_IP_e_a_saude_nunca_e_limitada()
    {
        await using var fabrica = ApiComLimite(porUsuario: 100, anonimo: 3);
        var cliente = Cliente(fabrica);

        for (var i = 0; i < 10; i++) (await cliente.GetAsync("/health")).StatusCode.ShouldBe(HttpStatusCode.OK);
        for (var i = 0; i < 3; i++) (await cliente.GetAsync("/api/acesso/eu")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await cliente.GetAsync("/api/acesso/eu")).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }
}
