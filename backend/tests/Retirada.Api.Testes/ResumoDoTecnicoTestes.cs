using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Retirada.Comum;
using Shouldly;

namespace Retirada.Api.Testes;

/// <summary>Resumo na tela do técnico: só o que é dele, contado pelo histórico da ordem.</summary>
[Collection(ColecaoDaApi.Nome)]
public class ResumoDoTecnicoTestes(ApiDeTeste api)
{
    private static readonly object Gps = new { latitude = -20.1234, longitude = -45.5678, precisaoM = 8 };

    private static async Task ComandoAsync(HttpClient cliente, Guid ordem, string acao, object corpo)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, $"/api/ordens/{ordem}/{acao}") { Content = JsonContent.Create(corpo) };
        pedido.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var resposta = await cliente.SendAsync(pedido);
        resposta.StatusCode.ShouldBe(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
    }

    private async Task<OrdemDeTeste> RetirarAsync(HttpClient tecnico, Guid tecnicoId)
    {
        var ordem = await api.ImportarOrdemAsync((o, agora) => { o.Liberar(agora); o.Designar(tecnicoId, agora); });
        var foto = await ApiDeTeste.FotoEnviadaAsync(tecnico, ordem.Id);
        await ComandoAsync(tecnico, ordem.Id, "retirada", new
        {
            itens = new object[] { new { equipamentoId = ordem.OnuId, numeroSerie = ordem.SerieDaOnu }, new { equipamentoId = ordem.FonteId, numeroSerie = (string?)null } },
            gps = Gps,
            fotos = new[] { new { id = foto, tipo = "equipamento", capturadaEm = DateTimeOffset.UtcNow } },
        });
        return ordem;
    }

    [FatoComBanco]
    public async Task Conta_retiradas_conferidas_visitas_e_pendentes_so_do_proprio_tecnico()
    {
        var (tecnico, eu) = await api.EntrarComoAsync(Papeis.Tecnico);
        var (outro, outroUsuario) = await api.EntrarComoAsync(Papeis.Tecnico);
        var (estoque, _) = await api.EntrarComoAsync(Papeis.Estoque);

        var conferida = await RetirarAsync(tecnico, eu.Id);
        await ComandoAsync(estoque, conferida.Id, "conferencia", new
        {
            itens = new object[] { new { equipamentoId = conferida.OnuId, numeroSerie = conferida.SerieDaOnu }, new { equipamentoId = conferida.FonteId, numeroSerie = (string?)null } },
        });
        await RetirarAsync(tecnico, eu.Id);                                    // retirada, esperando o estoque
        await api.ImportarOrdemAsync((o, agora) => { o.Liberar(agora); o.Designar(eu.Id, agora); }); // pendente
        var visitada = await api.ImportarOrdemAsync((o, agora) => { o.Liberar(agora); o.Designar(eu.Id, agora); });
        await ComandoAsync(tecnico, visitada.Id, "visita-sem-sucesso", new { motivo = "ausente", gps = Gps });
        await RetirarAsync(outro, outroUsuario.Id);                            // de outro técnico: não conta

        var resumo = await tecnico.GetFromJsonAsync<JsonElement>("/api/ordens/meu-resumo");

        resumo.GetProperty("pendentes").GetInt32().ShouldBe(1);
        resumo.GetProperty("aguardandoConferencia").GetInt32().ShouldBe(1);
        foreach (var periodo in new[] { "hoje", "semana", "mes" })
        {
            var c = resumo.GetProperty(periodo);
            c.GetProperty("retiradas").GetInt32().ShouldBe(2, periodo);
            c.GetProperty("conferidas").GetInt32().ShouldBe(1, periodo);
            c.GetProperty("visitasSemSucesso").GetInt32().ShouldBe(1, periodo);
        }
    }

    [FatoComBanco]
    public async Task So_o_tecnico_ve_o_proprio_resumo()
    {
        var (financeiro, _) = await api.EntrarComoAsync(Papeis.Financeiro);
        (await financeiro.GetAsync("/api/ordens/meu-resumo")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
