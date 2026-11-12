using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Retirada.Comum;
using Shouldly;

namespace Retirada.Api.Testes;

/// <summary>RF03 estendido: financeiro cadastra ordem direto no sistema, sem planilha.</summary>
[Collection(ColecaoDaApi.Nome)]
public class CadastroManualDeOrdemTestes(ApiDeTeste api)
{
    private static object PedidoValido() => new
    {
        cliente = "ASSINANTE SIMULADO 7",
        enderecoClienteHubsoft = (int?)null,
        observacao = "Portão branco.",
        endereco = new
        {
            logradouro = "RUA DOS IPÊS",
            numero = "120",
            complemento = (string?)null,
            bairro = "CENTRO",
            cidade = "CIDADE SIMULADA",
            uf = "MG",
            referencia = (string?)null,
        },
        cep = "30123-456",
        telefones = new[] { "(00) 98765-4321" },
        equipamentos = new[]
        {
            new
            {
                tipo = "ONT",
                modelo = "HG8145V5",
                serie = "SIMU0001",
                mac = "aa:bb:cc:dd:ee:ff",
                valor = 350m,
            },
        },
        tipoOs = "RETIRADA DE EQUIPAMENTO",
        carenciaHoras = 48,
    };

    private static Task<HttpResponseMessage> CadastrarAsync(HttpClient cliente, object corpo, Guid? chave = null, bool semChave = false)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, "/api/ordens") { Content = JsonContent.Create(corpo) };
        if (!semChave) pedido.Headers.Add("Idempotency-Key", (chave ?? Guid.NewGuid()).ToString());
        return cliente.SendAsync(pedido);
    }

    [FatoComBanco]
    public async Task Financeiro_cadastra_ordem_e_ela_entra_como_nova_e_em_carencia()
    {
        var (financeiro, _) = await api.EntrarComoAsync(Papeis.Financeiro);
        var resposta = await CadastrarAsync(financeiro, PedidoValido());
        resposta.StatusCode.ShouldBe(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());

        var ordem = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        ordem.GetProperty("status").GetString().ShouldBe("nova");
        ordem.GetProperty("endereco").GetProperty("cidade").GetString().ShouldBe("CIDADE SIMULADA");
        ordem.GetProperty("equipamentos").GetArrayLength().ShouldBe(1);
        // Entra no histórico com evento próprio, não com "importada".
        var eventos = ordem.GetProperty("historico").EnumerateArray().Select(e => e.GetProperty("tipo").GetString()).ToArray();
        eventos.ShouldContain("cadastrada_manualmente");
    }

    [FatoComBanco]
    public async Task Reenvio_com_a_mesma_chave_devolve_a_mesma_ordem_e_nao_cria_outra()
    {
        var (financeiro, _) = await api.EntrarComoAsync(Papeis.Financeiro);
        var chave = Guid.NewGuid();
        var primeira = await CadastrarAsync(financeiro, PedidoValido(), chave);
        primeira.StatusCode.ShouldBe(HttpStatusCode.OK);
        var idPrimeira = JsonDocument.Parse(await primeira.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        var segunda = await CadastrarAsync(financeiro, PedidoValido(), chave);
        segunda.StatusCode.ShouldBe(HttpStatusCode.OK);
        var idSegunda = JsonDocument.Parse(await segunda.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        idSegunda.ShouldBe(idPrimeira);
    }

    [FatoComBanco]
    public async Task Sem_cabecalho_de_idempotencia_recusa_com_IDEMPOTENCIA()
    {
        var (financeiro, _) = await api.EntrarComoAsync(Papeis.Financeiro);
        var resposta = await CadastrarAsync(financeiro, PedidoValido(), semChave: true);
        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement.GetProperty("codigo").GetString().ShouldBe("IDEMPOTENCIA");
    }

    [FatoComBanco]
    public async Task Sem_equipamento_recusa_com_RN02()
    {
        var (financeiro, _) = await api.EntrarComoAsync(Papeis.Financeiro);
        var pedido = new
        {
            cliente = "ASSINANTE SIMULADO 7",
            observacao = (string?)null,
            endereco = new { logradouro = "RUA A", numero = "1", complemento = (string?)null, bairro = "CENTRO", cidade = "CIDADE SIMULADA", uf = "MG", referencia = (string?)null },
            cep = (string?)null,
            telefones = Array.Empty<string>(),
            equipamentos = Array.Empty<object>(),
            tipoOs = (string?)null,
            carenciaHoras = (int?)null,
        };
        var resposta = await CadastrarAsync(financeiro, pedido);
        resposta.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement.GetProperty("codigo").GetString().ShouldBe("RN02");
    }

    [FatoComBanco]
    public async Task Tecnico_nao_cadastra_ordem()
    {
        var (tecnico, _) = await api.EntrarComoAsync(Papeis.Tecnico);
        var resposta = await CadastrarAsync(tecnico, PedidoValido());
        resposta.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
