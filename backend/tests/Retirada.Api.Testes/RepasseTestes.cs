using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Retirada.Comum;
using Shouldly;

namespace Retirada.Api.Testes;

/// <summary>Repasse ao técnico: R$ 5,00 por retirada conferida pelo estoque, pago pelo financeiro.</summary>
[Collection(ColecaoDaApi.Nome)]
public class RepasseTestes(ApiDeTeste api)
{
    private static readonly object Gps = new { latitude = -20.1234, longitude = -45.5678, precisaoM = 8 };

    private static async Task<HttpResponseMessage> ComandoAsync(HttpClient cliente, Guid ordem, string acao, object corpo)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, $"/api/ordens/{ordem}/{acao}") { Content = JsonContent.Create(corpo) };
        pedido.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return await cliente.SendAsync(pedido);
    }

    private static object Itens(OrdemDeTeste o, string serie) => new
    {
        itens = new object[] { new { equipamentoId = o.OnuId, numeroSerie = serie }, new { equipamentoId = o.FonteId, numeroSerie = (string?)null } },
    };

    /// <summary>Retira pelo app do técnico e, se pedido, confere no estoque com a série certa (ou errada).</summary>
    private async Task<OrdemDeTeste> RetirarAsync(HttpClient tecnico, Guid tecnicoId, HttpClient? estoque = null, bool serieErrada = false)
    {
        var ordem = await api.ImportarOrdemAsync((o, agora) => { o.Liberar(agora); o.Designar(tecnicoId, agora); });
        var foto = await ApiDeTeste.FotoEnviadaAsync(tecnico, ordem.Id);
        (await ComandoAsync(tecnico, ordem.Id, "retirada", new
        {
            itens = new object[] { new { equipamentoId = ordem.OnuId, numeroSerie = ordem.SerieDaOnu }, new { equipamentoId = ordem.FonteId, numeroSerie = (string?)null } },
            gps = Gps,
            fotos = new[] { new { id = foto, tipo = "equipamento", capturadaEm = DateTimeOffset.UtcNow } },
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
        if (estoque is not null)
            (await ComandoAsync(estoque, ordem.Id, "conferencia", Itens(ordem, serieErrada ? "SIMU0000DIFERENTE" : ordem.SerieDaOnu)))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
        return ordem;
    }

    private static async Task<string?> CodigoAsync(HttpResponseMessage resposta) =>
        (await resposta.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("codigo", out var c) ? c.GetString() : null;

    [FatoComBanco]
    public async Task So_a_retirada_conferida_vira_credito_e_o_pagamento_zera_o_saldo()
    {
        var (tecnico, eu) = await api.EntrarComoAsync(Papeis.Tecnico);
        var (estoque, _) = await api.EntrarComoAsync(Papeis.Estoque);
        var (financeiro, _) = await api.EntrarComoAsync(Papeis.Financeiro);

        await RetirarAsync(tecnico, eu.Id, estoque);                     // conferida: R$ 5,00
        await RetirarAsync(tecnico, eu.Id, estoque);                     // conferida: R$ 5,00
        await RetirarAsync(tecnico, eu.Id);                              // esperando o estoque: nada ainda
        await RetirarAsync(tecnico, eu.Id, estoque, serieErrada: true);  // divergência (RN08): nada ainda

        var meu = await tecnico.GetFromJsonAsync<JsonElement>("/api/repasses/meu");
        meu.GetProperty("valorPorRetiradaCentavos").GetInt32().ShouldBe(500);
        meu.GetProperty("aReceber").GetProperty("quantidade").GetInt32().ShouldBe(2);
        meu.GetProperty("aReceber").GetProperty("valorCentavos").GetInt32().ShouldBe(1000);

        var lista = await financeiro.GetFromJsonAsync<JsonElement>("/api/repasses");
        lista.EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == eu.Id).GetProperty("valorCentavos").GetInt32().ShouldBe(1000);
        var detalhe = await financeiro.GetFromJsonAsync<JsonElement>($"/api/repasses/{eu.Id}");
        detalhe.GetProperty("emAberto").GetArrayLength().ShouldBe(2);
        detalhe.GetProperty("emAberto")[0].GetProperty("numeroOs").GetInt32().ShouldBe(7001);

        var valorVelho = await financeiro.PostAsJsonAsync($"/api/repasses/{eu.Id}/pagamentos", new { valorCentavos = 500 });
        valorVelho.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodigoAsync(valorVelho)).ShouldBe("REPASSE_VALOR_MUDOU");

        var pago = await financeiro.PostAsJsonAsync($"/api/repasses/{eu.Id}/pagamentos", new { valorCentavos = 1000, observacao = "PIX" });
        pago.StatusCode.ShouldBe(HttpStatusCode.OK, await pago.Content.ReadAsStringAsync());
        (await pago.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quantidade").GetInt32().ShouldBe(2);

        var deNovo = await financeiro.PostAsJsonAsync($"/api/repasses/{eu.Id}/pagamentos", new { valorCentavos = 1000 });
        (await CodigoAsync(deNovo)).ShouldBe("REPASSE_SEM_SALDO"); // clique duplo não paga duas vezes

        meu = await tecnico.GetFromJsonAsync<JsonElement>("/api/repasses/meu");
        meu.GetProperty("aReceber").GetProperty("valorCentavos").GetInt32().ShouldBe(0);
        meu.GetProperty("ultimosPagamentos")[0].GetProperty("valorCentavos").GetInt32().ShouldBe(1000);
    }

    [FatoComBanco]
    public async Task Tecnico_nao_ve_o_repasse_dos_outros_e_financeiro_nao_tem_repasse_proprio()
    {
        var (tecnico, _) = await api.EntrarComoAsync(Papeis.Tecnico);
        var (financeiro, _) = await api.EntrarComoAsync(Papeis.Financeiro);

        (await tecnico.GetAsync("/api/repasses")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await tecnico.PostAsJsonAsync($"/api/repasses/{Guid.NewGuid()}/pagamentos", new { valorCentavos = 500 })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await financeiro.GetAsync("/api/repasses/meu")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await financeiro.GetAsync($"/api/repasses/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
