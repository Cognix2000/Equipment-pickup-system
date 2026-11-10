using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Retirada.Comum;
using Retirada.Ordens.Portas;
using Shouldly;

namespace Retirada.Api.Testes;

/// <summary>
/// Resgate por PIX do técnico terceiro (ADR-014): só o que o estoque conferiu, só para a chave aprovada, e quem
/// diz que o dinheiro caiu é o aviso do gateway. Mínimo de R$ 10,00 nos testes (ApiDeTeste).
/// </summary>
[Collection(ColecaoDaApi.Nome)]
public class ResgateTestes(ApiDeTeste api)
{
    // Chaves fictícias: e-mail .invalid e o CPF de exemplo dos manuais (123.456.789-09).
    private const string EmailPix = "terceiro.pix@exemplo.invalid";

    private async Task<(HttpClient Cliente, Guid Id)> TerceiroAsync(int creditosDeCincoReais = 0, bool comChaveAprovada = true)
    {
        var (cliente, usuario) = await api.EntrarComoAsync(Papeis.Tecnico);
        await api.EscalarAsync<object>("UPDATE retirada.usuario SET vinculo = 'terceiro' WHERE id = @id", ("id", usuario.Id));
        for (var i = 0; i < creditosDeCincoReais; i++)
            await api.EscalarAsync<object>("""
                INSERT INTO retirada.repasse_credito (id, tecnico_id, tipo, valor_centavos, creditado_em)
                VALUES (gen_random_uuid(), @id, 'ajuste', 500, now())
                """, ("id", usuario.Id));
        if (comChaveAprovada)
        {
            (await cliente.PostAsJsonAsync("/api/resgates/minha-chave-pix", new { tipo = "email", chave = EmailPix })).StatusCode.ShouldBe(HttpStatusCode.OK);
            await api.EscalarAsync<object>("UPDATE retirada.chave_pix SET situacao = 'aprovada' WHERE tecnico_id = @id AND situacao = 'pendente'", ("id", usuario.Id));
        }
        return (cliente, usuario.Id);
    }

    private static async Task<JsonElement> MeuAsync(HttpClient cliente) => await cliente.GetFromJsonAsync<JsonElement>("/api/resgates/meu");

    private static async Task<string?> CodigoAsync(HttpResponseMessage resposta) =>
        (await resposta.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("codigo", out var c) ? c.GetString() : null;

    private HttpRequestMessage Aviso(string caminho, object corpo, string? token = ApiDeTeste.TokenDoWebhook)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, $"/api/webhooks/asaas/{caminho}") { Content = JsonContent.Create(corpo) };
        if (token is not null) pedido.Headers.Add("asaas-access-token", token);
        return pedido;
    }

    /// <summary>O Asaas não manda o cabeçalho do app (X-Retirada) nem tem sessão.</summary>
    private HttpClient Asaas() => api.CreateClient();

    [FatoComBanco]
    public async Task Terceiro_resgata_o_saldo_e_o_aviso_do_gateway_marca_pago_com_o_id_de_fim_a_fim()
    {
        var (tecnico, eu) = await TerceiroAsync(creditosDeCincoReais: 3);
        var resposta = await tecnico.PostAsync("/api/resgates", null);
        resposta.StatusCode.ShouldBe(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());

        var pedido = api.Gateway.Pedidos.Last(p => p.Chave.Chave == EmailPix);
        pedido.ValorCentavos.ShouldBe(1500);
        var meu = await resposta.Content.ReadFromJsonAsync<JsonElement>();
        meu.GetProperty("disponivel").GetProperty("valorCentavos").GetInt32().ShouldBe(0); // reservado no resgate
        var resgate = meu.GetProperty("resgates")[0];
        resgate.GetProperty("situacao").GetString().ShouldBe("enviado");

        // Antes de soltar o PIX, o Asaas pergunta: bate com o resgate, então aprova.
        var gatewayId = $"tra_{pedido.ResgateId:N}";
        var validacao = await Asaas().SendAsync(Aviso("validacao", new
        {
            type = "TRANSFER",
            transfer = new { id = gatewayId, value = 15.0m, externalReference = pedido.ResgateId.ToString() },
        }));
        (await validacao.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().ShouldBe("APPROVED");

        // O PIX caiu: o aviso traz o ID de Fim a Fim e o comprovante.
        (await Asaas().SendAsync(Aviso("eventos", new
        {
            id = "evt_teste_1",
            @event = "TRANSFER_DONE",
            transfer = new
            {
                id = gatewayId, status = "DONE", value = 15.0m,
                endToEndIdentifier = "E00000000202611101200abcdefghijk",
                transactionReceiptUrl = "https://comprovante.exemplo.invalid/1",
            },
        }))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var depois = (await MeuAsync(tecnico)).GetProperty("resgates")[0];
        depois.GetProperty("situacao").GetString().ShouldBe("pago");
        depois.GetProperty("idFimAFim").GetString().ShouldBe("E00000000202611101200abcdefghijk");
        depois.GetProperty("comprovanteUrl").GetString().ShouldBe("https://comprovante.exemplo.invalid/1");

        // Vira pagamento do repasse: o técnico vê como os outros pagamentos, e nada sobra a receber.
        var repasse = await tecnico.GetFromJsonAsync<JsonElement>("/api/repasses/meu");
        repasse.GetProperty("aReceber").GetProperty("valorCentavos").GetInt32().ShouldBe(0);
        repasse.GetProperty("ultimosPagamentos")[0].GetProperty("valorCentavos").GetInt32().ShouldBe(1500);

        // O mesmo aviso de novo não paga duas vezes, e um PIX novo para o resgate já pago é recusado.
        (await Asaas().SendAsync(Aviso("eventos", new { @event = "TRANSFER_DONE", transfer = new { id = gatewayId, status = "DONE" } })))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await api.EscalarAsync<long>("SELECT count(*) FROM retirada.repasse_pagamento WHERE tecnico_id = @id", ("id", eu))).ShouldBe(1);
        var repetido = await Asaas().SendAsync(Aviso("validacao", new { type = "TRANSFER", transfer = new { id = "tra_outro", value = 15.0m, externalReference = pedido.ResgateId.ToString() } }));
        (await repetido.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().ShouldBe("REFUSED");
    }

    [FatoComBanco]
    public async Task Pix_que_falha_devolve_o_saldo_e_o_tecnico_pode_tentar_de_novo()
    {
        var (tecnico, _) = await TerceiroAsync(creditosDeCincoReais: 2);
        (await tecnico.PostAsync("/api/resgates", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var pedido = api.Gateway.Pedidos.Last(p => p.Chave.Chave == EmailPix);

        (await Asaas().SendAsync(Aviso("eventos", new
        {
            @event = "TRANSFER_FAILED",
            transfer = new { id = $"tra_{pedido.ResgateId:N}", status = "FAILED", failReason = "Chave PIX não encontrada" },
        }))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var meu = await MeuAsync(tecnico);
        meu.GetProperty("resgates")[0].GetProperty("situacao").GetString().ShouldBe("falhou");
        meu.GetProperty("resgates")[0].GetProperty("motivoFalha").GetString().ShouldBe("Chave PIX não encontrada");
        meu.GetProperty("disponivel").GetProperty("valorCentavos").GetInt32().ShouldBe(1000);

        // Gateway recusando na hora (ex.: sem saldo na conta): falha sem ficar pendurado.
        api.Gateway.Responder(_ => throw new GatewayRecusouException("Saldo insuficiente na conta."));
        var segunda = await (await tecnico.PostAsync("/api/resgates", null)).Content.ReadFromJsonAsync<JsonElement>();
        segunda.GetProperty("resgates")[0].GetProperty("situacao").GetString().ShouldBe("falhou");
        segunda.GetProperty("resgates")[0].GetProperty("motivoFalha").GetString().ShouldBe("Saldo insuficiente na conta.");
        segunda.GetProperty("disponivel").GetProperty("valorCentavos").GetInt32().ShouldBe(1000);
    }

    [FatoComBanco]
    public async Task Regras_clt_nao_resgata_sem_chave_aprovada_nao_abaixo_do_minimo_nao_e_um_por_vez()
    {
        var (clt, _) = await api.EntrarComoAsync(Papeis.Tecnico); // técnico novo entra como CLT
        (await MeuAsync(clt)).GetProperty("vinculo").GetString().ShouldBe("clt");
        (await CodigoAsync(await clt.PostAsync("/api/resgates", null))).ShouldBe("RESGATE_SO_TERCEIRO");
        (await CodigoAsync(await clt.PostAsJsonAsync("/api/resgates/minha-chave-pix", new { tipo = "email", chave = EmailPix }))).ShouldBe("RESGATE_SO_TERCEIRO");

        var (semChave, _) = await TerceiroAsync(creditosDeCincoReais: 4, comChaveAprovada: false);
        (await semChave.PostAsJsonAsync("/api/resgates/minha-chave-pix", new { tipo = "email", chave = EmailPix })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CodigoAsync(await semChave.PostAsync("/api/resgates", null))).ShouldBe("RESGATE_SEM_CHAVE"); // pendente não vale

        var (pouco, _) = await TerceiroAsync(creditosDeCincoReais: 1);
        (await CodigoAsync(await pouco.PostAsync("/api/resgates", null))).ShouldBe("RESGATE_ABAIXO_DO_MINIMO");

        var (emAndamento, eu) = await TerceiroAsync(creditosDeCincoReais: 2);
        (await emAndamento.PostAsync("/api/resgates", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await api.EscalarAsync<object>("""
            INSERT INTO retirada.repasse_credito (id, tecnico_id, tipo, valor_centavos, creditado_em) VALUES (gen_random_uuid(), @id, 'ajuste', 2000, now())
            """, ("id", eu));
        (await CodigoAsync(await emAndamento.PostAsync("/api/resgates", null))).ShouldBe("RESGATE_EM_ANDAMENTO");

        (await CodigoAsync(await emAndamento.PostAsJsonAsync("/api/resgates/minha-chave-pix", new { tipo = "cpf", chave = "123.456.789-00" })))
            .ShouldBe("PIX_CHAVE_INVALIDA");
    }

    [FatoComBanco]
    public async Task Financeiro_aprova_a_troca_de_chave_e_o_pagamento_manual_nao_paga_o_que_esta_no_resgate()
    {
        var (tecnico, eu) = await TerceiroAsync(creditosDeCincoReais: 2);
        var (financeiro, _) = await api.EntrarComoAsync(Papeis.Financeiro);

        (await tecnico.PostAsJsonAsync("/api/resgates/minha-chave-pix", new { tipo = "cpf", chave = "123.456.789-09" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var pendentes = await financeiro.GetFromJsonAsync<JsonElement>("/api/resgates/chaves-pix");
        var minha = pendentes.EnumerateArray().Single(c => c.GetProperty("tecnicoId").GetGuid() == eu);
        minha.GetProperty("chave").GetString().ShouldBe("12345678909");   // o financeiro vê inteira para conferir
        minha.GetProperty("trocaDeChave").GetBoolean().ShouldBeTrue();

        // Enquanto o financeiro não aprova, o resgate vai para a chave antiga.
        (await tecnico.PostAsync("/api/resgates", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        api.Gateway.Pedidos.Last(p => p.Chave.Chave is EmailPix or "12345678909").Chave.Chave.ShouldBe(EmailPix);

        // O pagamento manual não acha saldo: o que tinha está a caminho no PIX.
        var lista = await financeiro.GetFromJsonAsync<JsonElement>("/api/repasses");
        var naLista = lista.EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == eu);
        naLista.GetProperty("vinculo").GetString().ShouldBe("terceiro");
        naLista.GetProperty("valorCentavos").GetInt32().ShouldBe(0);
        (await CodigoAsync(await financeiro.PostAsJsonAsync($"/api/repasses/{eu}/pagamentos", new { valorCentavos = 1000 }))).ShouldBe("REPASSE_SEM_SALDO");

        (await financeiro.PostAsync($"/api/resgates/chaves-pix/{minha.GetProperty("id").GetGuid()}/aprovar", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var chaves = (await MeuAsync(tecnico)).GetProperty("chaves");
        chaves.GetArrayLength().ShouldBe(1);
        chaves[0].GetProperty("situacao").GetString().ShouldBe("aprovada");
        chaves[0].GetProperty("mascarada").GetString().ShouldBe("***.456.789-**");

        // Recusa com motivo: o técnico vê e cadastra outra.
        (await tecnico.PostAsJsonAsync("/api/resgates/minha-chave-pix", new { tipo = "telefone", chave = "(00) 91234-5678" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        var outra = (await financeiro.GetFromJsonAsync<JsonElement>("/api/resgates/chaves-pix")).EnumerateArray().Single(c => c.GetProperty("tecnicoId").GetGuid() == eu);
        outra.GetProperty("chave").GetString().ShouldBe("+5500912345678");
        (await financeiro.PostAsJsonAsync($"/api/resgates/chaves-pix/{outra.GetProperty("id").GetGuid()}/recusar", new { motivo = "Esse número não é seu." }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var recusada = (await MeuAsync(tecnico)).GetProperty("chaves").EnumerateArray().Single(c => c.GetProperty("situacao").GetString() == "recusada");
        recusada.GetProperty("motivoRecusa").GetString().ShouldBe("Esse número não é seu.");
    }

    [FatoComBanco]
    public async Task Aviso_sem_o_token_certo_e_recusado_e_pix_desconhecido_nao_e_aprovado()
    {
        (await Asaas().SendAsync(Aviso("eventos", new { @event = "TRANSFER_DONE" }, token: null))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Asaas().SendAsync(Aviso("validacao", new { type = "TRANSFER" }, token: "token-errado"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Quem roubar a chave da API e mandar um PIX por fora não passa da validação.
        var estranho = await Asaas().SendAsync(Aviso("validacao", new { type = "TRANSFER", transfer = new { id = "tra_desconhecida", value = 999.0m } }));
        (await estranho.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().ShouldBe("REFUSED");
        var boleto = await Asaas().SendAsync(Aviso("validacao", new { type = "BILL", bill = new { id = "bill_1" } }));
        (await boleto.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().ShouldBe("REFUSED");

        // Valor diferente do resgate também é recusado.
        var (tecnico, _) = await TerceiroAsync(creditosDeCincoReais: 2);
        (await tecnico.PostAsync("/api/resgates", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var pedido = api.Gateway.Pedidos.Last(p => p.Chave.Chave == EmailPix);
        var alterado = await Asaas().SendAsync(Aviso("validacao", new { type = "TRANSFER", transfer = new { id = $"tra_{pedido.ResgateId:N}", value = 100.0m } }));
        (await alterado.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().ShouldBe("REFUSED");

        // Outra transferência com a referência do resgate (já sabemos o id da nossa) e destino trocado: recusadas.
        var outraTransferencia = await Asaas().SendAsync(Aviso("validacao", new
        {
            type = "TRANSFER", transfer = new { id = "tra_de_outra_pessoa", value = 10.0m, externalReference = pedido.ResgateId.ToString() },
        }));
        (await outraTransferencia.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().ShouldBe("REFUSED");
        var outraChave = await Asaas().SendAsync(Aviso("validacao", new
        {
            type = "TRANSFER", transfer = new { id = $"tra_{pedido.ResgateId:N}", value = 10.0m, bankAccount = new { pixAddressKey = "golpista@exemplo.invalid" } },
        }));
        (await outraChave.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().ShouldBe("REFUSED");
        var certa = await Asaas().SendAsync(Aviso("validacao", new
        {
            type = "TRANSFER", transfer = new { id = $"tra_{pedido.ResgateId:N}", value = 10.0m, bankAccount = new { pixAddressKey = "Terceiro.Pix@Exemplo.Invalid" } },
        }));
        (await certa.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().ShouldBe("APPROVED");
    }

    [FatoComBanco]
    public async Task Financeiro_ve_a_folha_do_mes_dos_clt_e_a_lista_de_resgates()
    {
        var (_, clt) = await api.EntrarComoAsync(Papeis.Tecnico);
        for (var i = 0; i < 3; i++)
            await api.EscalarAsync<object>("""
                INSERT INTO retirada.repasse_credito (id, tecnico_id, tipo, valor_centavos, creditado_em) VALUES (gen_random_uuid(), @id, 'ajuste', 500, now())
                """, ("id", clt.Id));
        var (tecnico, terceiro) = await TerceiroAsync(creditosDeCincoReais: 2);
        (await tecnico.PostAsync("/api/resgates", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var (financeiro, _) = await api.EntrarComoAsync(Papeis.Financeiro);

        var folha = await financeiro.GetFromJsonAsync<JsonElement>("/api/repasses/folha");
        var tecnicos = folha.GetProperty("tecnicos").EnumerateArray().ToList();
        var noMes = tecnicos.Single(t => t.GetProperty("id").GetGuid() == clt.Id);
        noMes.GetProperty("valorCentavos").GetInt32().ShouldBe(1500);
        noMes.GetProperty("emAbertoCentavos").GetInt32().ShouldBe(1500);
        tecnicos.ShouldNotContain(t => t.GetProperty("id").GetGuid() == terceiro); // terceiro não vai para a folha

        var resgates = await financeiro.GetFromJsonAsync<JsonElement>("/api/resgates");
        resgates.EnumerateArray().ShouldContain(r => r.GetProperty("tecnicoId").GetGuid() == terceiro);
        (await financeiro.GetAsync("/api/repasses/folha?mes=novembro")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        (await tecnico.GetAsync("/api/resgates")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await tecnico.GetAsync("/api/resgates/chaves-pix")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await tecnico.GetAsync("/api/repasses/folha")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [FatoComBanco]
    public async Task Gestor_define_o_vinculo_do_tecnico_no_cadastro_e_depois()
    {
        var (gestor, _) = await api.EntrarComoAsync(Papeis.Gestor);
        var cadastro = await gestor.PostAsJsonAsync("/api/usuarios", new
        {
            nome = "Técnico Terceiro Teste", email = $"terceiro.{Guid.NewGuid():N}@exemplo.invalid", papel = "tecnico", vinculo = "terceiro",
        });
        cadastro.StatusCode.ShouldBe(HttpStatusCode.Created);
        var usuario = (await cadastro.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("usuario");
        usuario.GetProperty("vinculo").GetString().ShouldBe("terceiro");

        var mudou = await gestor.PostAsJsonAsync($"/api/usuarios/{usuario.GetProperty("id").GetGuid()}/vinculo", new { vinculo = "clt" });
        (await mudou.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("vinculo").GetString().ShouldBe("clt");

        var financeiro = await gestor.PostAsJsonAsync("/api/usuarios", new
        {
            nome = "Financeiro Teste", email = $"fin.{Guid.NewGuid():N}@exemplo.invalid", papel = "financeiro", vinculo = "terceiro",
        });
        (await CodigoAsync(financeiro)).ShouldBe("USUARIO_VINCULO");
    }

    [FatoComBanco]
    public async Task Conciliacao_resolve_o_resgate_parado()
    {
        var (tecnico, eu) = await TerceiroAsync(creditosDeCincoReais: 2);
        api.Gateway.Responder(_ => throw new TimeoutException("o gateway não respondeu"));
        var meu = await (await tecnico.PostAsync("/api/resgates", null)).Content.ReadFromJsonAsync<JsonElement>();
        meu.GetProperty("resgates")[0].GetProperty("situacao").GetString().ShouldBe("solicitado"); // sem saber se saiu

        // Envelhece o resgate e roda a conciliação, como a rotina faz.
        await api.EscalarAsync<object>("UPDATE retirada.resgate SET solicitado_em = now() - interval '11 minutes' WHERE tecnico_id = @id", ("id", eu));
        (await api.ConciliarResgatesAsync()).ShouldBeGreaterThanOrEqualTo(1);
        var depois = await MeuAsync(tecnico);
        depois.GetProperty("resgates")[0].GetProperty("situacao").GetString().ShouldBe("falhou");
        depois.GetProperty("disponivel").GetProperty("valorCentavos").GetInt32().ShouldBe(1000);

        // Enviado há mais de 30 min sem aviso: pergunta ao gateway, que diz que caiu.
        (await tecnico.PostAsync("/api/resgates", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var pedido = api.Gateway.Pedidos.Last(p => p.Chave.Chave == EmailPix);
        var gatewayId = $"tra_{pedido.ResgateId:N}";
        api.Gateway.Consultas[gatewayId] = new PixNoGateway(gatewayId, SituacaoNoGateway.Pago, "E00000000202611101300zyxwvutsrqp");
        await api.EscalarAsync<object>("UPDATE retirada.resgate SET solicitado_em = now() - interval '31 minutes' WHERE id = @id", ("id", pedido.ResgateId));
        await api.ConciliarResgatesAsync();
        var pago = (await MeuAsync(tecnico)).GetProperty("resgates").EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == pedido.ResgateId);
        pago.GetProperty("situacao").GetString().ShouldBe("pago");
        pago.GetProperty("idFimAFim").GetString().ShouldBe("E00000000202611101300zyxwvutsrqp");
    }
}
