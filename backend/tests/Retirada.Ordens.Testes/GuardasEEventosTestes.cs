using Retirada.Comum;
using Retirada.Ordens.Dominio;
using Shouldly;

namespace Retirada.Ordens.Testes;

/// <summary>
/// Cada transição proibida tem teste, e cada mudança gera o evento certo para o histórico (RN12).
/// Estes testes nasceram do teste de mutação (Stryker): ele mostrou guardas que podiam sumir sem ninguém notar.
/// </summary>
public class GuardasEEventosTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 13, 13, 15, 0, TimeSpan.Zero);
    private static readonly Localizacao NoCliente = new(-21.0005, -38.9990);

    private static Ordem Nova(Localizacao? localizacao = null) => Ordem.Importar(
        "os-9101", 5101, "REMOÇÃO EQUIP FTTH", Guid.NewGuid(),
        new Endereco("RUA DOS IPÊS", "120", null, "CENTRO", "CIDADE SIMULADA", "MG", null),
        localizacao, null,
        [
            Equipamento.Criar(TipoEquipamento.ONU, "Huawei EG8010H", NumeroSerie.Criar("HWTC1A2B3C4D")),
            Equipamento.Criar(TipoEquipamento.Fonte, "Fonte 12V", null),
        ],
        Agora, TimeSpan.FromHours(48));

    private static Ordem Disponivel(Localizacao? localizacao = null)
    {
        var o = Nova(localizacao);
        o.Liberar(Agora);
        return o;
    }

    private static Ordem Atribuida(Localizacao? localizacao = null)
    {
        var o = Disponivel(localizacao);
        o.Designar(Guid.NewGuid(), Agora);
        return o;
    }

    private static Ordem Concluida()
    {
        var o = Atribuida();
        o.RegistrarRetirada(Series(o), NoCliente, Agora);
        return o;
    }

    private static Ordem Conferida()
    {
        var o = Concluida();
        o.Conferir(Series(o), Guid.NewGuid(), Agora);
        return o;
    }

    private static Ordem Cancelada()
    {
        var o = Disponivel();
        o.Cancelar(MotivoCancelamento.Pagou, Agora);
        return o;
    }

    private static Dictionary<Guid, NumeroSerie?> Series(Ordem o) => o.Equipamentos.ToDictionary(e => e.Id, e => e.NumeroSerie);

    /// <summary>A regra barra com o código certo e uma mensagem que o front-end pode mostrar.</summary>
    private static RegraDeNegocioException DeveViolar(Action acao, string codigo)
    {
        var erro = Should.Throw<RegraDeNegocioException>(acao);
        erro.Codigo.ShouldBe(codigo);
        erro.Message.ShouldNotBeNullOrWhiteSpace();
        return erro;
    }

    [Fact] public void Liberar_so_ordem_nova() => DeveViolar(() => Disponivel().Liberar(Agora), "RN03");

    [Fact] public void Designar_so_ordem_disponivel() => DeveViolar(() => Nova().Designar(Guid.NewGuid(), Agora), "RF08");

    [Fact]
    public void Retirada_so_com_tecnico()
    {
        var o = Disponivel();
        DeveViolar(() => o.RegistrarRetirada(Series(o), NoCliente, Agora), "RF12");
    }

    [Fact]
    public void Retirada_incompleta_diz_qual_item_falta()
    {
        var o = Atribuida();
        var series = Series(o);
        series.Remove(o.Equipamentos[0].Id);

        DeveViolar(() => o.RegistrarRetirada(series, NoCliente, Agora), "RF12").Message.ShouldContain("Huawei EG8010H");
    }

    [Fact] public void Retirada_de_ordem_cancelada_e_RN07() => DeveViolar(() => Cancelada().RegistrarRetirada(new Dictionary<Guid, NumeroSerie?>(), NoCliente, Agora), "RN07");

    [Fact]
    public void Visita_so_com_tecnico() =>
        DeveViolar(() => Disponivel().RegistrarVisitaSemSucesso(MotivoVisitaSemSucesso.Ausente, 3, Agora), "RF13");

    [Fact] public void Conferir_so_depois_da_retirada() => DeveViolar(() => Atribuida().Conferir(new Dictionary<Guid, NumeroSerie?>(), Guid.NewGuid(), Agora), "RF14");

    public static TheoryData<string> PodemSerCanceladas() => ["Nova", "Disponivel", "Atribuida"];

    [Theory]
    [MemberData(nameof(PodemSerCanceladas))]
    public void Cancela_enquanto_o_equipamento_esta_com_o_cliente(string estado)
    {
        var o = estado switch { "Nova" => Nova(), "Disponivel" => Disponivel(), _ => Atribuida() };

        o.Cancelar(MotivoCancelamento.Renegociou, Agora);

        o.Status.ShouldBe(StatusOrdem.Cancelada);
        o.ResponsavelId.ShouldBeNull();
        o.EncerradaEm.ShouldBe(Agora);
    }

    [Fact] public void Nao_cancela_depois_da_retirada() => DeveViolar(() => Concluida().Cancelar(MotivoCancelamento.Pagou, Agora), "RF09");

    [Fact] public void Devolucao_na_loja_nao_vale_para_ordem_conferida() => DeveViolar(() => Conferida().RegistrarDevolucaoNaLoja(Agora), "RF15");

    [Fact] public void Devolucao_na_loja_nao_vale_para_ordem_cancelada() => DeveViolar(() => Cancelada().RegistrarDevolucaoNaLoja(Agora), "RF15");

    [Fact]
    public void Devolucao_na_loja_tira_a_ordem_do_tecnico()
    {
        var o = Atribuida();

        o.RegistrarDevolucaoNaLoja(Agora);

        o.ResponsavelId.ShouldBeNull();
        o.EncerradaEm.ShouldBe(Agora);
    }

    [Fact] public void Baixa_so_depois_da_retirada() => DeveViolar(() => Atribuida().MarcarBaixaFeita(Guid.NewGuid(), Agora), "RF19");

    [Fact]
    public void Caminho_completo_deixa_um_evento_por_passo()
    {
        var o = Conferida();
        o.MarcarBaixaFeita(Guid.NewGuid(), Agora);

        o.Eventos.Select(e => e.Tipo).ShouldBe(["importada", "liberada", "designada", "retirada", "conferida", "baixa_hubsoft"]);
        o.Eventos.ShouldAllBe(e => e.OcorridoEm == Agora);
    }

    [Fact]
    public void Eventos_guardam_o_motivo()
    {
        var visitada = Atribuida();
        visitada.RegistrarVisitaSemSucesso(MotivoVisitaSemSucesso.Recusou, 3, Agora);
        var cancelada = Cancelada();

        visitada.Eventos[^1].Tipo.ShouldBe("visita_sem_sucesso:Recusou");
        cancelada.Eventos[^1].Tipo.ShouldBe("cancelada:Pagou");
    }

    [Fact]
    public void Carencia_vencida_registra_o_evento()
    {
        var o = Nova();
        o.LiberarSeCarenciaVenceu(Agora.AddHours(48));
        o.Eventos[^1].Tipo.ShouldBe("carencia_vencida");
    }

    [Fact]
    public void Conferencia_com_divergencia_fica_no_historico_sem_fechar()
    {
        var o = Concluida();
        var lidas = Series(o);
        lidas[o.Equipamentos[0].Id] = NumeroSerie.Criar("HWTC00000000");

        o.Conferir(lidas, Guid.NewGuid(), Agora);

        o.Eventos[^1].Tipo.ShouldBe("divergencia_na_conferencia");
    }

    [Fact]
    public void Retirada_guarda_o_GPS_so_quando_a_ordem_nao_tinha_ponto()
    {
        var semPonto = Atribuida();
        var comPonto = Atribuida(new Localizacao(-21.1, -39.1));

        semPonto.RegistrarRetirada(Series(semPonto), NoCliente, Agora);
        comPonto.RegistrarRetirada(Series(comPonto), NoCliente, Agora);

        semPonto.Localizacao.ShouldBe(NoCliente);
        comPonto.Localizacao.ShouldBe(new Localizacao(-21.1, -39.1));
    }

    [Fact]
    public void Itens_mudam_de_situacao_na_retirada_e_na_conferencia()
    {
        var o = Concluida();
        o.Equipamentos.ShouldAllBe(e => e.Situacao == SituacaoEquipamento.Retirado);

        o.Conferir(Series(o), Guid.NewGuid(), Agora);
        o.Equipamentos.ShouldAllBe(e => e.Situacao == SituacaoEquipamento.Conferido);
    }

    [Fact] public void Importacao_exige_chave() =>
        DeveViolar(() => Ordem.Importar(" ", null, "REMOÇÃO EQUIP FTTH", Guid.NewGuid(),
            new Endereco("RUA", null, null, "CENTRO", "CIDADE", "MG", null), null, null, [], Agora, TimeSpan.FromHours(48)), "RN01");

    [Fact] public void Equipamento_exige_modelo() =>
        DeveViolar(() => Equipamento.Criar(TipoEquipamento.Fonte, "  ", null), "MODELO");

    [Fact]
    public void Equipamento_sem_serie_diz_o_tipo() =>
        DeveViolar(() => Equipamento.Criar(TipoEquipamento.Roteador, "TP-Link Archer C6", null), "SERIE").Message.ShouldContain("Roteador");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Serie_vazia_e_recusada(string? texto) => DeveViolar(() => NumeroSerie.Criar(texto), "SERIE");

    [Fact]
    public void Serie_aceita_ate_64_caracteres()
    {
        NumeroSerie.Criar(new string('A', 64)).Valor.Length.ShouldBe(64);
        DeveViolar(() => NumeroSerie.Criar(new string('A', 65)), "SERIE");
    }

    [Theory]
    [InlineData(-90, 0)]
    [InlineData(90, 0)]
    [InlineData(0, -180)]
    [InlineData(0, 180)]
    public void Coordenada_no_limite_e_aceita(double latitude, double longitude) =>
        Should.NotThrow(() => new Localizacao(latitude, longitude));

    [Theory]
    [InlineData(-90.0001, 0)]
    [InlineData(90.0001, 0)]
    [InlineData(0, -180.0001)]
    [InlineData(0, 180.0001)]
    public void Coordenada_fora_do_limite_e_recusada(double latitude, double longitude) =>
        DeveViolar(() => _ = new Localizacao(latitude, longitude), "LOCALIZACAO");
}
