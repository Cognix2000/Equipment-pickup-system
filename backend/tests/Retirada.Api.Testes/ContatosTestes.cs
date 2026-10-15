using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Retirada.Comum;
using Retirada.Ordens.Dominio;
using Shouldly;

namespace Retirada.Api.Testes;

/// <summary>RN10: tentativas de contato contra a API e o banco de verdade.</summary>
[Collection(ColecaoDaApi.Nome)]
public class ContatosTestes(ApiDeTeste api)
{
    private static Task<HttpResponseMessage> ContatarAsync(HttpClient cliente, Guid ordem, object corpo, Guid? chave = null)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, $"/api/ordens/{ordem}/contatos") { Content = JsonContent.Create(corpo) };
        pedido.Headers.Add("Idempotency-Key", (chave ?? Guid.NewGuid()).ToString());
        return cliente.SendAsync(pedido);
    }

    private static async Task<JsonElement> OkAsync(Task<HttpResponseMessage> envio)
    {
        var resposta = await envio;
        var corpo = await resposta.Content.ReadAsStringAsync();
        resposta.StatusCode.ShouldBe(HttpStatusCode.OK, corpo);
        return JsonDocument.Parse(corpo).RootElement;
    }

    private static async Task DeveRecusarAsync(Task<HttpResponseMessage> envio, HttpStatusCode status, string? codigo = null)
    {
        var resposta = await envio;
        var corpo = await resposta.Content.ReadAsStringAsync();
        resposta.StatusCode.ShouldBe(status, corpo);
        if (codigo is not null) JsonDocument.Parse(corpo).RootElement.GetProperty("codigo").GetString().ShouldBe(codigo);
    }

    /// <summary>Telefone fictício (DDD 00) do cliente da ordem.</summary>
    private async Task<Guid> TelefoneAsync(Guid ordem, string numero = "+5500991234567", bool formatoValido = true)
    {
        await using var conexao = new NpgsqlConnection(ApiDeTeste.Conexao);
        await conexao.OpenAsync();
        await using var comando = new NpgsqlCommand("""
            INSERT INTO retirada.telefone_cliente (cliente_id, numero_e164, como_veio, tipo, formato_valido)
            SELECT cliente_id, @numero, @numero, 'celular', @valido FROM retirada.ordem WHERE id = @ordem
            RETURNING id
            """, conexao);
        comando.Parameters.AddWithValue("numero", numero);
        comando.Parameters.AddWithValue("valido", formatoValido);
        comando.Parameters.AddWithValue("ordem", ordem);
        return (Guid)(await comando.ExecuteScalarAsync())!;
    }

    private static object Ligacao(Guid telefone, string resultado) => new { telefoneId = telefone, canal = "ligacao", resultado };

    [FatoComBanco]
    public async Task Tecnico_registra_a_ligacao_e_o_telefone_fica_marcado() // RN10
    {
        var (tecnico, tecnicoUsuario) = await api.EntrarComoAsync(Papeis.Tecnico);
        var ordem = await api.ImportarOrdemAsync((o, agora) => { o.Liberar(agora); o.Designar(tecnicoUsuario.Id, agora); });
        var telefone = await TelefoneAsync(ordem.Id);
        var foraDaJanela = !JanelaDeContato.Permite(DateTimeOffset.UtcNow);

        var resposta = await OkAsync(ContatarAsync(tecnico, ordem.Id, Ligacao(telefone, "numero_inexistente")));

        resposta.GetProperty("status").GetString().ShouldBe("atribuida"); // contato não muda a situação
        var noDetalhe = resposta.GetProperty("telefones").EnumerateArray().Single();
        noDetalhe.GetProperty("id").GetGuid().ShouldBe(telefone);
        noDetalhe.GetProperty("linha").GetString().ShouldBe("inexistente");
        noDetalhe.GetProperty("whatsapp").GetString().ShouldBe("nao_verificado");

        var evento = (await api.EventosAsync(ordem.Id)).ShouldHaveSingleItem();
        evento.Tipo.ShouldBe("contato");
        evento.Motivo.ShouldBe("numero_inexistente");
        evento.Ator.ShouldBe(tecnicoUsuario.Id);

        (await api.EscalarAsync<string>("""
            SELECT canal || '|' || resultado || '|' || feito_por || '|' || fora_da_janela
              FROM retirada.tentativa_contato WHERE ordem_id = @id
            """, ("id", ordem.Id)))
            .ShouldBe($"ligacao|numero_inexistente|{tecnicoUsuario.Id}|{(foraDaJanela ? "true" : "false")}");
        // O histórico guarda só o id do telefone, nunca o número.
        var dados = await api.EscalarAsync<string>("SELECT dados::text FROM retirada.evento_ordem WHERE ordem_id = @id", ("id", ordem.Id));
        dados.ShouldNotBeNull().ShouldContain(telefone.ToString());
        dados.ShouldNotContain("5500991234567");
    }

    [FatoComBanco]
    public async Task Financeiro_registra_o_WhatsApp_com_o_modelo() // RN10
    {
        var (financeiro, _) = await api.EntrarComoAsync(Papeis.Financeiro);
        var ordem = await api.ImportarOrdemAsync((o, agora) => o.Liberar(agora));
        var telefone = await TelefoneAsync(ordem.Id);

        var resposta = await OkAsync(ContatarAsync(financeiro, ordem.Id,
            new { telefoneId = telefone, canal = "whatsapp", resultado = "enviada", modelo = "aviso" }));

        var noDetalhe = resposta.GetProperty("telefones").EnumerateArray().Single();
        noDetalhe.GetProperty("whatsapp").GetString().ShouldBe("tem");
        noDetalhe.GetProperty("linha").GetString().ShouldBe("nao_verificada");
        (await api.EscalarAsync<string>("SELECT modelo FROM retirada.tentativa_contato WHERE ordem_id = @id", ("id", ordem.Id))).ShouldBe("aviso");
    }

    [FatoComBanco]
    public async Task Reenviar_a_mesma_chave_nao_duplica_a_tentativa() // ADR-009
    {
        var (financeiro, _) = await api.EntrarComoAsync(Papeis.Financeiro);
        var ordem = await api.ImportarOrdemAsync((o, agora) => o.Liberar(agora));
        var telefone = await TelefoneAsync(ordem.Id);
        var chave = Guid.NewGuid();

        await OkAsync(ContatarAsync(financeiro, ordem.Id, Ligacao(telefone, "nao_atendeu"), chave));
        await OkAsync(ContatarAsync(financeiro, ordem.Id, Ligacao(telefone, "nao_atendeu"), chave));

        (await api.EscalarAsync<long>("SELECT count(*) FROM retirada.tentativa_contato WHERE ordem_id = @id", ("id", ordem.Id))).ShouldBe(1);
    }

    [FatoComBanco]
    public async Task Recusa_o_que_a_regra_barra_sem_gravar_nada()
    {
        var (financeiro, _) = await api.EntrarComoAsync(Papeis.Financeiro);
        var emCarencia = await api.ImportarOrdemAsync();
        var disponivel = await api.ImportarOrdemAsync((o, agora) => o.Liberar(agora));
        var telefoneEmCarencia = await TelefoneAsync(emCarencia.Id);
        var telefone = await TelefoneAsync(disponivel.Id);
        var foraDoPadrao = await TelefoneAsync(disponivel.Id, "00 1234", formatoValido: false);

        await DeveRecusarAsync(ContatarAsync(financeiro, emCarencia.Id, Ligacao(telefoneEmCarencia, "atendeu")), HttpStatusCode.UnprocessableEntity, "RN10");
        await DeveRecusarAsync(ContatarAsync(financeiro, disponivel.Id, Ligacao(telefone, "respondeu")), HttpStatusCode.UnprocessableEntity, "RN10");
        await DeveRecusarAsync(ContatarAsync(financeiro, disponivel.Id, Ligacao(foraDoPadrao, "atendeu")), HttpStatusCode.UnprocessableEntity, "RN05");
        // Telefone de outro cliente não serve, nem para descobrir se o id existe.
        await DeveRecusarAsync(ContatarAsync(financeiro, disponivel.Id, Ligacao(telefoneEmCarencia, "atendeu")), HttpStatusCode.UnprocessableEntity, "RN10");

        (await api.EscalarAsync<long>("SELECT count(*) FROM retirada.tentativa_contato WHERE ordem_id IN (@a, @b)",
            ("a", emCarencia.Id), ("b", disponivel.Id))).ShouldBe(0);
        (await api.EventosAsync(disponivel.Id)).ShouldBeEmpty();
    }

    [FatoComBanco]
    public async Task So_quem_pode_contatar_e_so_na_ordem_dele() // RNF04
    {
        var (estoque, _) = await api.EntrarComoAsync(Papeis.Estoque);
        var (outroTecnico, _) = await api.EntrarComoAsync(Papeis.Tecnico);
        var dono = await api.CriarUsuarioAsync(Papeis.Tecnico);
        var ordem = await api.ImportarOrdemAsync((o, agora) => { o.Liberar(agora); o.Designar(dono.Id, agora); });
        var telefone = await TelefoneAsync(ordem.Id);

        await DeveRecusarAsync(ContatarAsync(estoque, ordem.Id, Ligacao(telefone, "atendeu")), HttpStatusCode.Forbidden);
        await DeveRecusarAsync(ContatarAsync(outroTecnico, ordem.Id, Ligacao(telefone, "atendeu")), HttpStatusCode.NotFound);
    }
}
