using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Retirada.Comum;
using Retirada.Ordens.Dominio;
using Shouldly;

namespace Retirada.Api.Testes;

/// <summary>Comandos da ordem (ADR-009) contra a API e o banco de verdade.</summary>
[Collection(ColecaoDaApi.Nome)]
public class ComandosDaOrdemTestes(ApiDeTeste api)
{
    private static readonly object GpsNoCliente = new { latitude = -20.1234, longitude = -45.5678, precisaoM = 8 };

    private static Task<HttpResponseMessage> EnviarAsync(HttpClient cliente, Guid ordem, string acao, object? corpo = null, Guid? chave = null, bool semChave = false)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, $"/api/ordens/{ordem}/{acao}") { Content = JsonContent.Create(corpo ?? new { }) };
        if (!semChave) pedido.Headers.Add("Idempotency-Key", (chave ?? Guid.NewGuid()).ToString());
        return cliente.SendAsync(pedido);
    }

    private static async Task<JsonElement> OkAsync(Task<HttpResponseMessage> envio)
    {
        var resposta = await envio;
        var corpo = await resposta.Content.ReadAsStringAsync();
        resposta.StatusCode.ShouldBe(HttpStatusCode.OK, corpo);
        return JsonDocument.Parse(corpo).RootElement;
    }

    private static async Task DeveRecusarAsync(Task<HttpResponseMessage> envio, HttpStatusCode status, string codigo)
    {
        var resposta = await envio;
        var corpo = await resposta.Content.ReadAsStringAsync();
        resposta.StatusCode.ShouldBe(status, corpo);
        JsonDocument.Parse(corpo).RootElement.GetProperty("codigo").GetString().ShouldBe(codigo);
    }

    private static object Retirada(OrdemDeTeste o, string? serieDaOnu = null) => new
    {
        itens = new object[]
        {
            new { equipamentoId = o.OnuId, numeroSerie = serieDaOnu ?? o.SerieDaOnu },
            new { equipamentoId = o.FonteId, numeroSerie = (string?)null },
        },
        gps = GpsNoCliente,
    };

    private static object Conferencia(OrdemDeTeste o, string serieDaOnu) => new
    {
        itens = new object[] { new { equipamentoId = o.OnuId, numeroSerie = serieDaOnu }, new { equipamentoId = o.FonteId, numeroSerie = (string?)null } },
    };

    private static void RetirarPeloDominio(Ordem o, OrdemDeTeste dados, DateTimeOffset agora) =>
        o.RegistrarRetirada(new Dictionary<Guid, NumeroSerie?> { [dados.OnuId] = NumeroSerie.Criar(dados.SerieDaOnu), [dados.FonteId] = null },
            new Localizacao(-20.1, -45.5), agora);

    [FatoComBanco]
    public async Task Fluxo_completo_da_retirada_grava_um_evento_por_acao() // RN03, RF08, RF12, RF14, RF19
    {
        var (gestor, _) = await api.EntrarComoAsync(Papeis.Gestor);
        var (financeiro, _) = await api.EntrarComoAsync(Papeis.Financeiro);
        var (tecnico, tecnicoUsuario) = await api.EntrarComoAsync(Papeis.Tecnico);
        var (estoque, _) = await api.EntrarComoAsync(Papeis.Estoque);
        var ordem = await api.ImportarOrdemAsync();

        (await OkAsync(EnviarAsync(financeiro, ordem.Id, "liberar"))).GetProperty("status").GetString().ShouldBe("disponivel");

        var designada = await OkAsync(EnviarAsync(gestor, ordem.Id, "designar", new { tecnicoId = tecnicoUsuario.Id }));
        designada.GetProperty("status").GetString().ShouldBe("atribuida");
        designada.GetProperty("responsavelId").GetGuid().ShouldBe(tecnicoUsuario.Id);

        // A série vem como o técnico digitou: com espaço e minúscula, e ainda confere.
        var digitada = $" {ordem.SerieDaOnu[..6].ToLowerInvariant()} {ordem.SerieDaOnu[6..]}";
        var retirada = await OkAsync(EnviarAsync(tecnico, ordem.Id, "retirada", Retirada(ordem, digitada)));
        retirada.GetProperty("status").GetString().ShouldBe("concluida");
        retirada.GetProperty("aguardaBaixaNoHubsoft").GetBoolean().ShouldBeTrue(); // RF19

        (await OkAsync(EnviarAsync(estoque, ordem.Id, "conferencia", Conferencia(ordem, ordem.SerieDaOnu))))
            .GetProperty("status").GetString().ShouldBe("conferida");

        (await OkAsync(EnviarAsync(financeiro, ordem.Id, "baixa-hubsoft")))
            .GetProperty("aguardaBaixaNoHubsoft").GetBoolean().ShouldBeFalse();

        var eventos = await api.EventosAsync(ordem.Id);
        eventos.Select(e => e.Tipo).ShouldBe(["liberada", "designada", "retirada", "conferida", "baixa_hubsoft"]);
        eventos.Select(e => e.Seq).ShouldBe([1, 2, 3, 4, 5]);
        eventos.Single(e => e.Tipo == "retirada").ShouldSatisfyAllConditions(
            e => e.Ator.ShouldBe(tecnicoUsuario.Id),
            e => e.ComLocal.ShouldBeTrue()); // GPS do aparelho no evento (RF12)
    }

    [FatoComBanco]
    public async Task Reenviar_a_mesma_chave_nao_repete_a_acao() // ADR-009
    {
        var (tecnico, tecnicoUsuario) = await api.EntrarComoAsync(Papeis.Tecnico);
        var ordem = await api.ImportarOrdemAsync((o, agora) => { o.Liberar(agora); o.Designar(tecnicoUsuario.Id, agora); });
        var chave = Guid.NewGuid();

        (await OkAsync(EnviarAsync(tecnico, ordem.Id, "retirada", Retirada(ordem), chave))).GetProperty("status").GetString().ShouldBe("concluida");
        (await OkAsync(EnviarAsync(tecnico, ordem.Id, "retirada", Retirada(ordem), chave))).GetProperty("status").GetString().ShouldBe("concluida");

        (await api.EventosAsync(ordem.Id)).Count(e => e.Tipo == "retirada").ShouldBe(1);
    }

    [FatoComBanco]
    public async Task Duas_designacoes_ao_mesmo_tempo_so_uma_vence() // concorrência otimista pela versão da ordem
    {
        var (gestor, _) = await api.EntrarComoAsync(Papeis.Gestor);
        var primeiro = await api.CriarUsuarioAsync(Papeis.Tecnico);
        var segundo = await api.CriarUsuarioAsync(Papeis.Tecnico);
        var ordem = await api.ImportarOrdemAsync((o, agora) => o.Liberar(agora));

        var respostas = await Task.WhenAll(
            EnviarAsync(gestor, ordem.Id, "designar", new { tecnicoId = primeiro.Id }),
            EnviarAsync(gestor, ordem.Id, "designar", new { tecnicoId = segundo.Id }));

        // Quem perde recebe 409 (chegaram juntas) ou 422 RF08 (a outra já tinha gravado): nunca as duas passam.
        respostas.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        respostas.Single(r => r.StatusCode != HttpStatusCode.OK).StatusCode
            .ShouldBeOneOf(HttpStatusCode.Conflict, HttpStatusCode.UnprocessableEntity);
        (await api.EventosAsync(ordem.Id)).Count(e => e.Tipo == "designada").ShouldBe(1);
    }

    [FatoComBanco]
    public async Task Sem_chave_de_idempotencia_o_comando_e_recusado()
    {
        var (financeiro, _) = await api.EntrarComoAsync(Papeis.Financeiro);
        var ordem = await api.ImportarOrdemAsync();

        await DeveRecusarAsync(EnviarAsync(financeiro, ordem.Id, "liberar", semChave: true), HttpStatusCode.BadRequest, "IDEMPOTENCIA");
    }

    [FatoComBanco]
    public async Task Chave_ja_usada_em_outra_ordem_e_recusada()
    {
        var (financeiro, _) = await api.EntrarComoAsync(Papeis.Financeiro);
        var primeira = await api.ImportarOrdemAsync();
        var segunda = await api.ImportarOrdemAsync();
        var chave = Guid.NewGuid();

        await OkAsync(EnviarAsync(financeiro, primeira.Id, "liberar", chave: chave));
        await DeveRecusarAsync(EnviarAsync(financeiro, segunda.Id, "liberar", chave: chave), HttpStatusCode.UnprocessableEntity, "IDEMPOTENCIA");
    }

    [FatoComBanco]
    public async Task Regra_violada_responde_422_com_o_codigo_da_regra()
    {
        var (gestor, _) = await api.EntrarComoAsync(Papeis.Gestor);
        var tecnico = await api.CriarUsuarioAsync(Papeis.Tecnico);
        var emCarencia = await api.ImportarOrdemAsync();

        await DeveRecusarAsync(EnviarAsync(gestor, emCarencia.Id, "designar", new { tecnicoId = tecnico.Id }), HttpStatusCode.UnprocessableEntity, "RF08");
        (await api.EventosAsync(emCarencia.Id)).ShouldBeEmpty(); // nada gravado quando a regra barra
    }

    [FatoComBanco]
    public async Task Designar_so_para_tecnico_ativo() // RF08
    {
        var (gestor, _) = await api.EntrarComoAsync(Papeis.Gestor);
        var estoquista = await api.CriarUsuarioAsync(Papeis.Estoque);
        var ordem = await api.ImportarOrdemAsync((o, agora) => o.Liberar(agora));

        await DeveRecusarAsync(EnviarAsync(gestor, ordem.Id, "designar", new { tecnicoId = estoquista.Id }), HttpStatusCode.UnprocessableEntity, "RF08");
        await DeveRecusarAsync(EnviarAsync(gestor, ordem.Id, "designar", new { tecnicoId = Guid.NewGuid() }), HttpStatusCode.UnprocessableEntity, "RF08");
    }

    [FatoComBanco]
    public async Task Tecnico_nao_mexe_na_ordem_de_outro_tecnico() // RNF04
    {
        var dono = await api.CriarUsuarioAsync(Papeis.Tecnico);
        var (outro, _) = await api.EntrarComoAsync(Papeis.Tecnico);
        var ordem = await api.ImportarOrdemAsync((o, agora) => { o.Liberar(agora); o.Designar(dono.Id, agora); });

        (await EnviarAsync(outro, ordem.Id, "retirada", Retirada(ordem))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [FatoComBanco]
    public async Task Cada_acao_exige_o_papel_certo()
    {
        var (tecnico, _) = await api.EntrarComoAsync(Papeis.Tecnico);
        var (estoque, _) = await api.EntrarComoAsync(Papeis.Estoque);
        var (financeiro, _) = await api.EntrarComoAsync(Papeis.Financeiro);
        var ordem = await api.ImportarOrdemAsync();

        (await EnviarAsync(tecnico, ordem.Id, "liberar")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await EnviarAsync(estoque, ordem.Id, "cancelar", new { motivo = "pagou" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await EnviarAsync(financeiro, ordem.Id, "conferencia", Conferencia(ordem, ordem.SerieDaOnu))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await EnviarAsync(financeiro, ordem.Id, "designar", new { tecnicoId = Guid.NewGuid() })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [FatoComBanco]
    public async Task Ordem_cancelada_bloqueia_a_retirada_do_tecnico_com_RN07() // RF09, RN07 e UC04
    {
        var (gestor, _) = await api.EntrarComoAsync(Papeis.Gestor);
        var (financeiro, _) = await api.EntrarComoAsync(Papeis.Financeiro);
        var (tecnico, tecnicoUsuario) = await api.EntrarComoAsync(Papeis.Tecnico);
        var ordem = await api.ImportarOrdemAsync((o, agora) => o.Liberar(agora));
        await OkAsync(EnviarAsync(gestor, ordem.Id, "designar", new { tecnicoId = tecnicoUsuario.Id }));

        (await OkAsync(EnviarAsync(financeiro, ordem.Id, "cancelar", new { motivo = "pagou" }))).GetProperty("status").GetString().ShouldBe("cancelada");

        await DeveRecusarAsync(EnviarAsync(tecnico, ordem.Id, "retirada", Retirada(ordem)), HttpStatusCode.UnprocessableEntity, "RN07");
        (await api.EventosAsync(ordem.Id)).Last().ShouldSatisfyAllConditions(
            e => e.Tipo.ShouldBe("cancelada"),
            e => e.Motivo.ShouldBe("pagou"));
    }

    [FatoComBanco]
    public async Task Conferencia_com_serie_diferente_nao_fecha_a_ordem() // RN08 e UC06
    {
        var tecnico = await api.CriarUsuarioAsync(Papeis.Tecnico);
        var (estoque, _) = await api.EntrarComoAsync(Papeis.Estoque);
        OrdemDeTeste? dados = null;
        var ordem = await api.ImportarOrdemAsync((o, agora) =>
        {
            o.Liberar(agora);
            o.Designar(tecnico.Id, agora);
            dados = new OrdemDeTeste(o.Id, o.Equipamentos[0].Id, o.Equipamentos[0].NumeroSerie!.Valor, o.Equipamentos[1].Id);
            RetirarPeloDominio(o, dados, agora);
        });

        var conferida = await OkAsync(EnviarAsync(estoque, ordem.Id, "conferencia", Conferencia(ordem, "SIMUOUTRASERIE01")));

        conferida.GetProperty("status").GetString().ShouldBe("concluida");
        conferida.GetProperty("equipamentos").EnumerateArray()
            .Single(e => e.GetProperty("id").GetGuid() == ordem.OnuId)
            .GetProperty("situacao").GetString().ShouldBe("divergente");
        (await api.EventosAsync(ordem.Id)).Single().Tipo.ShouldBe("divergencia_na_conferencia");
    }

    [FatoComBanco]
    public async Task Visita_sem_sucesso_devolve_a_ordem_para_disponivel() // RF13 e RN09
    {
        var (tecnico, tecnicoUsuario) = await api.EntrarComoAsync(Papeis.Tecnico);
        var ordem = await api.ImportarOrdemAsync((o, agora) => { o.Liberar(agora); o.Designar(tecnicoUsuario.Id, agora); });

        var visitada = await OkAsync(EnviarAsync(tecnico, ordem.Id, "visita-sem-sucesso", new { motivo = "nao_achou", gps = GpsNoCliente }));

        visitada.GetProperty("status").GetString().ShouldBe("disponivel");
        visitada.GetProperty("visitasSemSucesso").GetInt32().ShouldBe(1);
        visitada.GetProperty("responsavelId").ValueKind.ShouldBe(JsonValueKind.Null);
        (await api.EventosAsync(ordem.Id)).Single().ShouldSatisfyAllConditions(
            e => e.Tipo.ShouldBe("visita_sem_sucesso"),
            e => e.Motivo.ShouldBe("nao_achou"),
            e => e.ComLocal.ShouldBeTrue());
    }
}
