using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Retirada.Comum;
using Shouldly;

namespace Retirada.Api.Testes;

/// <summary>RF15: devolução na loja, de todos ou de parte dos itens, contra a API e o banco.</summary>
[Collection(ColecaoDaApi.Nome)]
public class DevolucaoNaLojaTestes(ApiDeTeste api)
{
    private static async Task<JsonElement> EnviarAsync(HttpClient cliente, Guid ordem, string acao, object corpo, HttpStatusCode esperado = HttpStatusCode.OK)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, $"/api/ordens/{ordem}/{acao}") { Content = JsonContent.Create(corpo) };
        pedido.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var resposta = await cliente.SendAsync(pedido);
        var texto = await resposta.Content.ReadAsStringAsync();
        resposta.StatusCode.ShouldBe(esperado, texto);
        return JsonDocument.Parse(texto).RootElement;
    }

    private static string Situacao(JsonElement ordem, Guid item) =>
        ordem.GetProperty("equipamentos").EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == item).GetProperty("situacao").GetString()!;

    [FatoComBanco]
    public async Task Fonte_na_loja_e_o_tecnico_retira_so_a_ONU()
    {
        var (estoque, _) = await api.EntrarComoAsync(Papeis.Estoque);
        var (tecnico, usuario) = await api.EntrarComoAsync(Papeis.Tecnico);
        var ordem = await api.ImportarOrdemAsync((o, agora) => { o.Liberar(agora); o.Designar(usuario.Id, agora); });

        var parcial = await EnviarAsync(estoque, ordem.Id, "devolucao-na-loja", new { itens = new[] { ordem.FonteId } });
        parcial.GetProperty("status").GetString().ShouldBe("atribuida"); // segue com o técnico
        Situacao(parcial, ordem.FonteId).ShouldBe("devolvido_na_loja");
        Situacao(parcial, ordem.OnuId).ShouldBe("pendente");

        // O técnico registra só a ONU; mandar a fonte de novo é recusado.
        var foto = await ApiDeTeste.FotoEnviadaAsync(tecnico, ordem.Id);
        var fotos = new[] { new { id = foto, tipo = "equipamento", capturadaEm = DateTimeOffset.UtcNow } };
        var gps = new { latitude = -20.1, longitude = -45.5, precisaoM = 8 };
        var comFonte = await EnviarAsync(tecnico, ordem.Id, "retirada", new
        {
            itens = new object[] { new { equipamentoId = ordem.OnuId, numeroSerie = ordem.SerieDaOnu }, new { equipamentoId = ordem.FonteId, numeroSerie = (string?)null } },
            gps, fotos,
        }, HttpStatusCode.UnprocessableEntity);
        comFonte.GetProperty("codigo").GetString().ShouldBe("RF12");

        var retirada = await EnviarAsync(tecnico, ordem.Id, "retirada", new
        {
            itens = new object[] { new { equipamentoId = ordem.OnuId, numeroSerie = ordem.SerieDaOnu } },
            gps, fotos,
        });
        retirada.GetProperty("status").GetString().ShouldBe("concluida");
        Situacao(retirada, ordem.FonteId).ShouldBe("devolvido_na_loja");

        var eventos = await api.EventosAsync(ordem.Id);
        eventos.Select(e => e.Tipo).ShouldBe(["devolucao_parcial_na_loja", "retirada"]);
    }

    [FatoComBanco]
    public async Task Sem_lista_devolve_tudo_o_que_esta_com_o_cliente_e_encerra()
    {
        var (estoque, _) = await api.EntrarComoAsync(Papeis.Estoque);
        var ordem = await api.ImportarOrdemAsync((o, agora) => o.Liberar(agora));

        var resposta = await EnviarAsync(estoque, ordem.Id, "devolucao-na-loja", new { });

        resposta.GetProperty("status").GetString().ShouldBe("devolvida_na_loja");
        (await api.EscalarAsync<string>("SELECT dados->>'encerrou' FROM retirada.evento_ordem WHERE ordem_id = @id", ("id", ordem.Id))).ShouldBe("true");
    }
}
