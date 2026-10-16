using Retirada.Comum;
using Retirada.Ordens.Dominio;
using Shouldly;

namespace Retirada.Ordens.Testes;

public class OrdemTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 13, 13, 15, 0, TimeSpan.Zero);
    private static readonly Guid Tecnico = Guid.NewGuid();
    private static readonly Localizacao NoCliente = new(-21.0005, -38.9990);

    private static (Ordem Ordem, Equipamento Onu, Equipamento Roteador, Equipamento Fonte) NovaOrdem()
    {
        var onu = Equipamento.Criar(TipoEquipamento.ONU, "Huawei EG8010H", NumeroSerie.Criar("HWTC1A2B3C4D"));
        var roteador = Equipamento.Criar(TipoEquipamento.Roteador, "TP-Link Archer C6", NumeroSerie.Criar("22361A2B3C4D5"));
        var fonte = Equipamento.Criar(TipoEquipamento.Fonte, "Fonte 12V", null);
        var ordem = Ordem.Importar(
            "os-9101", 5101, "REMOÇÃO EQUIP FTTH", Guid.NewGuid(),
            new Endereco("RUA DOS IPÊS", "120", "CASA 2", "CENTRO", "CIDADE SIMULADA", "MG", "PORTÃO AZUL"),
            NoCliente, "Cliente só fica em casa à tarde", [onu, roteador, fonte], Agora, TimeSpan.FromHours(48));
        return (ordem, onu, roteador, fonte);
    }

    private static Ordem Atribuida()
    {
        var ordem = NovaOrdem().Ordem;
        ordem.Liberar(Agora);
        ordem.Designar(Tecnico, Agora);
        return ordem;
    }

    private static Dictionary<Guid, NumeroSerie?> SeriesCorretas(Ordem ordem) =>
        ordem.Equipamentos.ToDictionary(e => e.Id, e => e.NumeroSerie);

    [Fact]
    public void Importada_fica_Nova_com_carencia_de_48h_contada_da_importacao() // RN03
    {
        var ordem = NovaOrdem().Ordem;

        ordem.Status.ShouldBe(StatusOrdem.Nova);
        ordem.CarenciaAte.ShouldBe(Agora.AddHours(48));
    }

    [Fact]
    public void Carencia_vencida_torna_a_ordem_Disponivel_so_depois_do_prazo()
    {
        var ordem = NovaOrdem().Ordem;

        ordem.LiberarSeCarenciaVenceu(Agora.AddHours(47)).ShouldBeFalse();
        ordem.LiberarSeCarenciaVenceu(Agora.AddHours(48)).ShouldBeTrue();
        ordem.Status.ShouldBe(StatusOrdem.Disponivel);
    }

    [Fact]
    public void Cada_mudanca_sobe_a_versao_para_a_concorrencia_otimista()
    {
        var ordem = NovaOrdem().Ordem;
        var antes = ordem.Versao;

        ordem.Liberar(Agora);

        ordem.Versao.ShouldBe(antes + 1);
    }

    [Fact]
    public void Ordem_cancelada_bloqueia_a_retirada() // RN07
    {
        var ordem = Atribuida();
        ordem.Cancelar(MotivoCancelamento.Pagou, Agora);

        var erro = Should.Throw<RegraDeNegocioException>(() => ordem.RegistrarRetirada(SeriesCorretas(ordem), NoCliente, Agora, FotoDeTeste.DoConjunto));

        erro.Codigo.ShouldBe("RN07");
    }

    [Fact]
    public void Retirada_exige_todos_os_itens()
    {
        var ordem = Atribuida();
        var series = SeriesCorretas(ordem);
        series.Remove(ordem.Equipamentos[0].Id);

        Should.Throw<RegraDeNegocioException>(() => ordem.RegistrarRetirada(series, NoCliente, Agora, FotoDeTeste.DoConjunto));
    }

    [Fact]
    public void Serie_diferente_na_retirada_vira_divergencia_sem_impedir() // UC04
    {
        var ordem = Atribuida();
        var series = SeriesCorretas(ordem);
        series[ordem.Equipamentos[1].Id] = NumeroSerie.Criar("22361A2B3C4D9");

        // RN14: a divergência vai com a foto da etiqueta que o técnico encontrou.
        ordem.RegistrarRetirada(series, NoCliente, Agora,
            [.. FotoDeTeste.DoConjunto, new FotoDaRetirada(TipoDeFoto.EtiquetaSerie, ordem.Equipamentos[1].Id)]);

        ordem.Status.ShouldBe(StatusOrdem.Concluida);
        ordem.Equipamentos[1].Situacao.ShouldBe(SituacaoEquipamento.Divergente);
        ordem.AguardaBaixaNoHubsoft.ShouldBeTrue(); // RF19: entra na lista do financeiro
    }

    [Fact]
    public void Serie_e_comparada_sem_diferenca_de_espaco_ou_caixa()
    {
        NumeroSerie.Criar(" hwtc 1a2b3c4d ").ShouldBe(NumeroSerie.Criar("HWTC1A2B3C4D"));
    }

    [Theory] // a etiqueta GPON traz o SN também com o fabricante em hexadecimal
    [InlineData("485754437C8D9E0F", "HWTC7C8D9E0F")]
    [InlineData(" 48575443 7c8d9e0f ", "HWTC7C8D9E0F")]
    [InlineData("5A544547A1B2C3D4", "ZTEGA1B2C3D4")]
    public void Sn_gpon_em_hexadecimal_e_o_mesmo_sn(string digitado, string naEtiqueta)
    {
        NumeroSerie.Criar(digitado).ShouldBe(NumeroSerie.Criar(naEtiqueta));
        NumeroSerie.Criar(digitado).Valor.ShouldBe(naEtiqueta);
    }

    [Theory] // só converte quando os 4 primeiros bytes são letras de fabricante
    [InlineData("0123456789ABCDEF")]
    [InlineData("485754437C8D9E0")]    // 15 caracteres
    [InlineData("485754437C8D9E0G")]   // não é hexadecimal
    [InlineData("22361A2B3C4D5")]      // série de roteador
    public void Outras_series_ficam_como_estao(string serie)
    {
        NumeroSerie.Criar(serie).Valor.ShouldBe(serie);
    }

    [Fact]
    public void Conferencia_com_divergencia_nao_fecha_a_ordem() // RN08
    {
        var ordem = Atribuida();
        ordem.RegistrarRetirada(SeriesCorretas(ordem), NoCliente, Agora, FotoDeTeste.DoConjunto);
        var lidas = SeriesCorretas(ordem);
        lidas[ordem.Equipamentos[1].Id] = NumeroSerie.Criar("22361A2B3C4D9");

        var resultado = ordem.Conferir(lidas, Guid.NewGuid(), Agora);

        resultado.Fechou.ShouldBeFalse();
        resultado.Divergentes.Single().Modelo.ShouldBe("TP-Link Archer C6");
        ordem.Status.ShouldBe(StatusOrdem.Concluida);
        ordem.EncerradaEm.ShouldBeNull();
    }

    [Fact]
    public void Conferencia_sem_divergencia_fecha_como_Conferida()
    {
        var ordem = Atribuida();
        ordem.RegistrarRetirada(SeriesCorretas(ordem), NoCliente, Agora, FotoDeTeste.DoConjunto);

        var resultado = ordem.Conferir(SeriesCorretas(ordem), Guid.NewGuid(), Agora);

        resultado.Fechou.ShouldBeTrue();
        ordem.Status.ShouldBe(StatusOrdem.Conferida);
        ordem.EncerradaEm.ShouldBe(Agora); // o CHECK do banco exige encerrada_em nos estados finais
    }

    [Theory]
    [InlineData(1, StatusOrdem.Disponivel)]
    [InlineData(3, StatusOrdem.EncerradaSemRetirada)]
    public void Visita_sem_sucesso_volta_para_Disponivel_ou_encerra_no_limite(int visitas, StatusOrdem esperado) // RN09
    {
        var ordem = Atribuida();

        for (var i = 1; i <= visitas; i++)
        {
            if (i > 1) ordem.Designar(Tecnico, Agora);
            ordem.RegistrarVisitaSemSucesso(MotivoVisitaSemSucesso.Ausente, limiteVisitas: 3, Agora);
        }

        ordem.Status.ShouldBe(esperado);
        ordem.ResponsavelId.ShouldBeNull();
    }

    [Fact]
    public void Devolucao_na_loja_vale_depois_de_encerrada_sem_retirada() // RN13
    {
        var ordem = Atribuida();
        for (var i = 0; i < 3; i++)
        {
            if (i > 0) ordem.Designar(Tecnico, Agora);
            ordem.RegistrarVisitaSemSucesso(MotivoVisitaSemSucesso.Recusou, 3, Agora);
        }

        ordem.RegistrarDevolucaoNaLoja([.. ordem.Equipamentos.Select(e => e.Id)], Agora).ShouldBeTrue();

        ordem.Status.ShouldBe(StatusOrdem.DevolvidaNaLoja);
        ordem.Equipamentos.ShouldAllBe(e => e.Situacao == SituacaoEquipamento.DevolvidoNaLoja);
    }

    [Fact]
    public void Baixa_no_HubSoft_so_uma_vez() // RF19
    {
        var ordem = Atribuida();
        ordem.RegistrarRetirada(SeriesCorretas(ordem), NoCliente, Agora, FotoDeTeste.DoConjunto);

        ordem.MarcarBaixaFeita(Guid.NewGuid(), Agora);

        ordem.AguardaBaixaNoHubsoft.ShouldBeFalse();
        Should.Throw<RegraDeNegocioException>(() => ordem.MarcarBaixaFeita(Guid.NewGuid(), Agora));
    }

    [Fact]
    public void Equipamento_patrimonial_exige_serie_e_fonte_nao()
    {
        Should.Throw<RegraDeNegocioException>(() => Equipamento.Criar(TipoEquipamento.ONT, "Huawei EG8145V5", null));
        Should.NotThrow(() => Equipamento.Criar(TipoEquipamento.Fonte, "Fonte 12V", null));
    }
}
