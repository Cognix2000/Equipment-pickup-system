using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Retirada.Comum;
using Shouldly;

namespace Retirada.Api.Testes;

/// <summary>Localização do técnico: ele manda a última posição; só o gestor vê a equipe.</summary>
[Collection(ColecaoDaApi.Nome)]
public class LocalizacaoTestes(ApiDeTeste api)
{
    private static JsonElement DoTecnico(JsonElement equipe, Guid id) =>
        equipe.EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == id);

    [FatoComBanco]
    public async Task Gestor_ve_a_ultima_posicao_do_tecnico_e_quantas_ordens_estao_com_ele()
    {
        var (tecnico, eu) = await api.EntrarComoAsync(Papeis.Tecnico);
        var (gestor, _) = await api.EntrarComoAsync(Papeis.Gestor);
        await api.ImportarOrdemAsync((o, agora) => { o.Liberar(agora); o.Designar(eu.Id, agora); });

        var antes = DoTecnico(await gestor.GetFromJsonAsync<JsonElement>("/api/localizacao/equipe"), eu.Id);
        antes.GetProperty("latitude").ValueKind.ShouldBe(JsonValueKind.Null); // app ainda não abriu

        var agora = DateTimeOffset.UtcNow;
        (await tecnico.PostAsJsonAsync("/api/localizacao/minha", new { latitude = -20.10, longitude = -45.50, precisaoM = 12, registradaEm = agora }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await tecnico.PostAsJsonAsync("/api/localizacao/minha", new { latitude = -20.20, longitude = -45.60, precisaoM = 8, registradaEm = agora.AddSeconds(30) }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        // Chegou atrasada (o celular estava sem sinal): não volta o técnico para trás.
        (await tecnico.PostAsJsonAsync("/api/localizacao/minha", new { latitude = -20.99, longitude = -45.99, precisaoM = 5, registradaEm = agora.AddSeconds(10) }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var depois = DoTecnico(await gestor.GetFromJsonAsync<JsonElement>("/api/localizacao/equipe"), eu.Id);
        depois.GetProperty("latitude").GetDouble().ShouldBe(-20.20, 0.000001);
        depois.GetProperty("longitude").GetDouble().ShouldBe(-45.60, 0.000001);
        depois.GetProperty("precisaoM").GetDouble().ShouldBe(8);
        depois.GetProperty("ordensComEle").GetInt32().ShouldBe(1);
        (await api.EscalarAsync<long>("SELECT count(*) FROM retirada.posicao_tecnico WHERE tecnico_id = @id", ("id", eu.Id))).ShouldBe(1); // sem trajeto
    }

    [FatoComBanco]
    public async Task Coordenada_invalida_e_recusada_e_sem_precisao_tambem_vale()
    {
        var (tecnico, _) = await api.EntrarComoAsync(Papeis.Tecnico);

        (await tecnico.PostAsJsonAsync("/api/localizacao/minha", new { latitude = -120.0, longitude = -45.5 }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await tecnico.PostAsJsonAsync("/api/localizacao/minha", new { latitude = -20.1, longitude = -45.5 }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [FatoComBanco]
    public async Task So_o_gestor_ve_a_equipe_e_so_o_tecnico_manda_posicao()
    {
        var (tecnico, _) = await api.EntrarComoAsync(Papeis.Tecnico);
        var (financeiro, _) = await api.EntrarComoAsync(Papeis.Financeiro);
        var (estoque, _) = await api.EntrarComoAsync(Papeis.Estoque);

        (await tecnico.GetAsync("/api/localizacao/equipe")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await financeiro.GetAsync("/api/localizacao/equipe")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await estoque.GetAsync("/api/localizacao/equipe")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await financeiro.PostAsJsonAsync("/api/localizacao/minha", new { latitude = -20.1, longitude = -45.5 })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await tecnico.GetFromJsonAsync<JsonElement>("/api/mapa")).GetProperty("estilo").GetString().ShouldBe("simulado");
    }

    [FatoComBanco]
    public async Task Visita_sem_sucesso_da_o_pino_a_ordem_sem_coordenada()
    {
        var (tecnico, eu) = await api.EntrarComoAsync(Papeis.Tecnico);
        var ordem = await api.ImportarOrdemAsync((o, agora) => { o.Liberar(agora); o.Designar(eu.Id, agora); });
        var pedido = new HttpRequestMessage(HttpMethod.Post, $"/api/ordens/{ordem.Id}/visita-sem-sucesso")
        {
            Content = JsonContent.Create(new { motivo = "ausente", gps = new { latitude = -20.1234, longitude = -45.5678, precisaoM = 8 } }),
        };
        pedido.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        (await tecnico.SendAsync(pedido)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var (gestor, _) = await api.EntrarComoAsync(Papeis.Gestor);
        var detalhe = await gestor.GetFromJsonAsync<JsonElement>($"/api/ordens/{ordem.Id}");
        detalhe.GetProperty("localizacao").GetProperty("latitude").GetDouble().ShouldBe(-20.1234, 0.000001);
    }
}
