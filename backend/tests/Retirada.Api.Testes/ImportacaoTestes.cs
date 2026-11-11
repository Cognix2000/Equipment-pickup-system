using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Retirada.Comum;
using Shouldly;

namespace Retirada.Api.Testes;

/// <summary>Importação pela API (UC01): prévia, confirmação, reimportação e pendências, contra o banco de verdade.</summary>
[Collection(ColecaoDaApi.Nome)]
public class ImportacaoTestes(ApiDeTeste api)
{
    private const string Cabecalho =
        "nome_razaosocial;data_inicio_programado;hora_inicio_programado;tipo_ordem_servico;endereco;numero;bairro;cidade;estado;telefone_primario;id_ordem_servico;equipamento_tipo;equipamento_modelo;numero_serie";

    /// <summary>Ids e séries novos a cada teste: o banco de teste guarda o que as rodadas anteriores gravaram.</summary>
    private sealed class Ids
    {
        private static int _proximo = Random.Shared.Next(100_000, 1_000_000_000);
        public int Os() => Interlocked.Increment(ref _proximo);
        public static string Serie() => $"SIMU{Guid.NewGuid():N}"[..16].ToUpperInvariant();
    }

    private static string Linha(int idOs, string cliente, string tipoOs = "REMOÇÃO EQUIP FTTH", string numero = "120", string bairro = "CENTRO",
        string equipamento = "ONU", string? serie = null) =>
        $"{cliente};15/09/2026;09:00:00;{tipoOs};RUA DOS IPÊS;{numero};{bairro};CIDADE SIMULADA;MG;(00) 91234-5678;{idOs};{equipamento};Modelo Teste;{serie ?? Ids.Serie()}";

    private static byte[] Csv(params string[] linhas) => Encoding.UTF8.GetBytes(Cabecalho + "\n" + string.Join("\n", linhas));

    private static MultipartFormDataContent Formulario(byte[] conteudo, string nome = "ordens.csv")
    {
        var arquivo = new ByteArrayContent(conteudo);
        arquivo.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        return new MultipartFormDataContent { { arquivo, "arquivo", nome } };
    }

    private static async Task<JsonElement> OkAsync(HttpResponseMessage resposta)
    {
        var corpo = await resposta.Content.ReadAsStringAsync();
        resposta.StatusCode.ShouldBe(HttpStatusCode.OK, corpo);
        return JsonDocument.Parse(corpo).RootElement;
    }

    private static async Task DeveRecusarAsync(HttpResponseMessage resposta, HttpStatusCode status, string codigo)
    {
        var corpo = await resposta.Content.ReadAsStringAsync();
        resposta.StatusCode.ShouldBe(status, corpo);
        JsonDocument.Parse(corpo).RootElement.GetProperty("codigo").GetString().ShouldBe(codigo);
    }

    private static async Task<JsonElement> PreverAsync(HttpClient cliente, byte[] arquivo) =>
        await OkAsync(await cliente.PostAsync("/api/importacoes/previa", Formulario(arquivo)));

    private static async Task<JsonElement> ConfirmarAsync(HttpClient cliente, JsonElement previa, byte[] arquivo) =>
        await OkAsync(await cliente.PostAsync($"/api/importacoes/{previa.GetProperty("importacaoId").GetGuid()}/confirmar", Formulario(arquivo)));

    private static int Resumo(JsonElement previa, string campo) => previa.GetProperty("resumo").GetProperty(campo).GetInt32();

    private Task<Guid> OrdemDaOsAsync(int idOs) => api.EscalarAsync<Guid>("SELECT id FROM retirada.ordem WHERE chave_importacao = @c", ("c", $"os:{idOs}"));

    [FatoComBanco]
    public async Task Previa_mostra_cada_linha_e_a_confirmacao_grava_tudo_de_uma_vez() // UC01, RF03 e RF05
    {
        var (financeiro, financeiroUsuario) = await api.EntrarComoAsync(Papeis.Financeiro);
        var ids = new Ids();
        var (a, b, c, d) = (ids.Os(), ids.Os(), ids.Os(), ids.Os());
        var arquivo = Csv(
            Linha(a, "ANA EXEMPLO", equipamento: "ONU"),
            Linha(a, "ANA EXEMPLO", equipamento: "Roteador"),            // mesma OS: mais um equipamento
            Linha(b, "BETO EXEMPLO"),
            Linha(c, "CAIO EXEMPLO", tipoOs: "REMOÇÃO EQUIP BLOQUEIO TEMPORARIO"), // RN02: pendência
            Linha(d, "DUDA EXEMPLO", tipoOs: "TROCA DE PLANO"));          // tipo desconhecido: erro, fica fora

        var previa = await PreverAsync(financeiro, arquivo);

        previa.GetProperty("formato").GetString().ShouldBe("csv");
        (Resumo(previa, "novas"), Resumo(previa, "pendencias"), Resumo(previa, "erros"), Resumo(previa, "ordensNovas")).ShouldBe((3, 1, 1, 2));
        (await OrdemDaOsAsync(a)).ShouldBe(Guid.Empty); // a prévia não grava nada

        var relatorio = await ConfirmarAsync(financeiro, previa, arquivo);

        (relatorio.GetProperty("ordensCriadas").GetInt32(), relatorio.GetProperty("pendenciasRegistradas").GetInt32()).ShouldBe((2, 1));
        var ordemA = await OrdemDaOsAsync(a);
        (await api.EscalarAsync<string>("SELECT status FROM retirada.ordem WHERE id = @id", ("id", ordemA))).ShouldBe("nova"); // RN03: carência
        (await api.EscalarAsync<long>("SELECT count(*) FROM retirada.equipamento_ordem WHERE ordem_id = @id", ("id", ordemA))).ShouldBe(2);
        (await api.EscalarAsync<string>(
            "SELECT t.numero_e164 FROM retirada.telefone_cliente t JOIN retirada.ordem o ON o.cliente_id = t.cliente_id WHERE o.id = @id", ("id", ordemA)))
            .ShouldBe("+5500912345678"); // RN05
        var importada = (await api.EventosAsync(ordemA)).Single();
        (importada.Tipo, importada.Ator).ShouldBe(("importada", financeiroUsuario.Id));
        (await OrdemDaOsAsync(d)).ShouldBe(Guid.Empty); // linha com erro ficou fora
        (await api.EscalarAsync<string>("SELECT motivo FROM retirada.pendencia_importacao WHERE chave_importacao = @c AND resolvida_em IS NULL", ("c", $"os:{c}")))
            .ShouldBe("bloqueio_temporario");
    }

    [FatoComBanco]
    public async Task Reenviar_o_mesmo_arquivo_nao_duplica_e_planilha_nova_so_atualiza_o_que_mudou() // RN01 e C02
    {
        var (financeiro, _) = await api.EntrarComoAsync(Papeis.Financeiro);
        var idOs = new Ids().Os();
        var serie = Ids.Serie();
        var arquivo = Csv(Linha(idOs, "ANA EXEMPLO", serie: serie));
        await ConfirmarAsync(financeiro, await PreverAsync(financeiro, arquivo), arquivo);

        var repetida = await PreverAsync(financeiro, arquivo);
        (Resumo(repetida, "repetidas"), Resumo(repetida, "ordensNovas")).ShouldBe((1, 0));
        (await ConfirmarAsync(financeiro, repetida, arquivo)).GetProperty("ordensCriadas").GetInt32().ShouldBe(0);

        var comMudanca = Csv(Linha(idOs, "ANA EXEMPLO", numero: "122", serie: serie));
        var previa = await PreverAsync(financeiro, comMudanca);
        previa.GetProperty("linhas")[0].GetProperty("detalhes")[0].GetString().ShouldBe("mudou: endereço");
        (await ConfirmarAsync(financeiro, previa, comMudanca)).GetProperty("ordensAtualizadas").GetInt32().ShouldBe(1);

        var ordem = await OrdemDaOsAsync(idOs);
        (await api.EscalarAsync<string>("SELECT numero FROM retirada.ordem WHERE id = @id", ("id", ordem))).ShouldBe("122");
        (await api.EventosAsync(ordem)).Select(e => e.Tipo).ShouldBe(["importada", "atualizada_pela_importacao"]);
    }

    [FatoComBanco]
    public async Task Pendencia_e_resolvida_quando_a_rescisao_chega() // RN02 e C02
    {
        var (financeiro, _) = await api.EntrarComoAsync(Papeis.Financeiro);
        var idOs = new Ids().Os();
        var bloqueio = Csv(Linha(idOs, "ANA EXEMPLO", tipoOs: "REMOÇÃO EQUIP BLOQUEIO TEMPORARIO"));
        await ConfirmarAsync(financeiro, await PreverAsync(financeiro, bloqueio), bloqueio);

        var rescisao = Csv(Linha(idOs, "ANA EXEMPLO"));
        var previa = await PreverAsync(financeiro, rescisao);
        previa.GetProperty("linhas")[0].GetProperty("detalhes")[0].GetString().ShouldBe("resolve a pendência: a rescisão chegou");
        (await ConfirmarAsync(financeiro, previa, rescisao)).GetProperty("pendenciasResolvidas").GetInt32().ShouldBe(1);

        var ordem = await OrdemDaOsAsync(idOs);
        (await api.EscalarAsync<Guid>("SELECT ordem_id FROM retirada.pendencia_importacao WHERE chave_importacao = @c", ("c", $"os:{idOs}"))).ShouldBe(ordem);
    }

    [FatoComBanco]
    public async Task Detalhe_da_ordem_importada_traz_telefones_e_historico() // RF07
    {
        var (financeiro, _) = await api.EntrarComoAsync(Papeis.Financeiro);
        var idOs = new Ids().Os();
        var arquivo = Csv(Linha(idOs, "ANA EXEMPLO"));
        await ConfirmarAsync(financeiro, await PreverAsync(financeiro, arquivo), arquivo);

        var detalhe = await OkAsync(await financeiro.GetAsync($"/api/ordens/{await OrdemDaOsAsync(idOs)}"));

        var telefone = detalhe.GetProperty("telefones").EnumerateArray().Single();
        (telefone.GetProperty("numero").GetString(), telefone.GetProperty("tipo").GetString(), telefone.GetProperty("formatoValido").GetBoolean())
            .ShouldBe(("+5500912345678", "celular", true));
        var evento = detalhe.GetProperty("historico").EnumerateArray().Single();
        (evento.GetProperty("tipo").GetString(), evento.GetProperty("statusPara").GetString(), evento.GetProperty("atorPapel").GetString())
            .ShouldBe(("importada", "nova", "financeiro"));
    }

    [FatoComBanco]
    public async Task Confirmar_exige_o_mesmo_arquivo_e_vale_uma_vez_so()
    {
        var (financeiro, _) = await api.EntrarComoAsync(Papeis.Financeiro);
        var ids = new Ids();
        var arquivo = Csv(Linha(ids.Os(), "ANA EXEMPLO"));
        var previa = await PreverAsync(financeiro, arquivo);
        var rota = $"/api/importacoes/{previa.GetProperty("importacaoId").GetGuid()}/confirmar";

        await DeveRecusarAsync(await financeiro.PostAsync(rota, Formulario(Csv(Linha(ids.Os(), "OUTRO")))), HttpStatusCode.UnprocessableEntity, "IMPORTACAO");
        await ConfirmarAsync(financeiro, previa, arquivo);
        await DeveRecusarAsync(await financeiro.PostAsync(rota, Formulario(arquivo)), HttpStatusCode.UnprocessableEntity, "IMPORTACAO");
    }

    [FatoComBanco]
    public async Task Previa_descartada_nao_pode_ser_confirmada()
    {
        var (financeiro, _) = await api.EntrarComoAsync(Papeis.Financeiro);
        var arquivo = Csv(Linha(new Ids().Os(), "ANA EXEMPLO"));
        var previa = await PreverAsync(financeiro, arquivo);
        var id = previa.GetProperty("importacaoId").GetGuid();

        (await financeiro.PostAsync($"/api/importacoes/{id}/descartar", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await DeveRecusarAsync(await financeiro.PostAsync($"/api/importacoes/{id}/confirmar", Formulario(arquivo)), HttpStatusCode.UnprocessableEntity, "IMPORTACAO");
    }

    [FatoComBanco]
    public async Task Arquivo_recusado_inteiro_diz_o_motivo() // RF04 e RN15
    {
        var (financeiro, _) = await api.EntrarComoAsync(Papeis.Financeiro);
        // Faltar o cliente barra o arquivo inteiro (único obrigatório junto com logradouro e cidade, após RF04 de nov/2026).
        var semCliente = Encoding.UTF8.GetBytes(Cabecalho.Replace("nome_razaosocial;", string.Empty) + "\n15/09/2026;09:00:00;REMOÇÃO EQUIP FTTH;RUA;1;CENTRO;CIDADE;MG;00912345678;1;;;");

        await DeveRecusarAsync(await financeiro.PostAsync("/api/importacoes/previa", Formulario(semCliente)), HttpStatusCode.UnprocessableEntity, "RF04");
        await DeveRecusarAsync(await financeiro.PostAsync("/api/importacoes/previa", Formulario("SELECT 1"u8.ToArray(), "backup.sql")), HttpStatusCode.UnprocessableEntity, "RN15");
    }

    [FatoComBanco]
    public async Task So_financeiro_e_gestor_importam()
    {
        var (tecnico, _) = await api.EntrarComoAsync(Papeis.Tecnico);
        var (estoque, _) = await api.EntrarComoAsync(Papeis.Estoque);
        var arquivo = Csv(Linha(new Ids().Os(), "ANA EXEMPLO"));

        (await tecnico.PostAsync("/api/importacoes/previa", Formulario(arquivo))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await estoque.PostAsync("/api/importacoes/previa", Formulario(arquivo))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
