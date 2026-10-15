using Retirada.Comum;
using Retirada.Ordens.Dominio;
using Shouldly;

namespace Retirada.Ordens.Testes;

/// <summary>RF15: o cliente leva à loja só parte dos itens; a ordem segue aberta com o resto.</summary>
public class DevolucaoParcialTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 15, 14, 0, 0, TimeSpan.Zero);
    private static readonly Localizacao NoCliente = new(-21.0005, -38.9990);

    private static Ordem ComTecnico()
    {
        var o = Ordem.Importar(
            "os-9601", 5601, "REMOÇÃO EQUIP FTTH", Guid.NewGuid(),
            new Endereco("RUA DOS IPÊS", "120", null, "CENTRO", "CIDADE SIMULADA", "MG", null),
            null, null,
            [
                Equipamento.Criar(TipoEquipamento.ONU, "Huawei EG8010H", NumeroSerie.Criar("HWTC6A6B6C6D")),
                Equipamento.Criar(TipoEquipamento.Roteador, "TP-Link Archer C6", NumeroSerie.Criar("22361A6B6C6D6")),
                Equipamento.Criar(TipoEquipamento.Fonte, "Fonte 12V", null),
            ],
            Agora, TimeSpan.FromHours(48));
        o.Liberar(Agora);
        o.Designar(Guid.NewGuid(), Agora);
        return o;
    }

    private static Equipamento Item(Ordem o, TipoEquipamento tipo) => o.Equipamentos.Single(e => e.Tipo == tipo);

    private static void DeveViolar(Action acao, string codigo, string trecho)
    {
        var erro = Should.Throw<RegraDeNegocioException>(acao);
        erro.Codigo.ShouldBe(codigo);
        erro.Message.ShouldContain(trecho);
    }

    [Fact]
    public void Parte_dos_itens_mantem_a_ordem_aberta_com_o_tecnico()
    {
        var o = ComTecnico();
        var tecnico = o.ResponsavelId;
        var versao = o.Versao;

        o.RegistrarDevolucaoNaLoja([Item(o, TipoEquipamento.Roteador).Id], Agora).ShouldBeFalse();

        o.Status.ShouldBe(StatusOrdem.Atribuida);
        o.ResponsavelId.ShouldBe(tecnico);
        o.EncerradaEm.ShouldBeNull();
        o.Versao.ShouldBe(versao + 1);
        o.Eventos[^1].Tipo.ShouldBe("devolucao_parcial_na_loja");
        Item(o, TipoEquipamento.Roteador).Situacao.ShouldBe(SituacaoEquipamento.DevolvidoNaLoja);
        Item(o, TipoEquipamento.ONU).Situacao.ShouldBe(SituacaoEquipamento.Pendente);
    }

    [Fact]
    public void Devolvendo_o_que_faltava_a_ordem_encerra()
    {
        var o = ComTecnico();
        o.RegistrarDevolucaoNaLoja([Item(o, TipoEquipamento.Roteador).Id], Agora);

        o.RegistrarDevolucaoNaLoja([Item(o, TipoEquipamento.ONU).Id, Item(o, TipoEquipamento.Fonte).Id], Agora).ShouldBeTrue();

        o.Status.ShouldBe(StatusOrdem.DevolvidaNaLoja);
        o.ResponsavelId.ShouldBeNull();
        o.EncerradaEm.ShouldBe(Agora);
        o.Eventos[^1].Tipo.ShouldBe("devolvida_na_loja");
    }

    [Fact]
    public void Retirada_depois_da_devolucao_parcial_leva_so_o_que_ficou_com_o_cliente()
    {
        var o = ComTecnico();
        var roteador = Item(o, TipoEquipamento.Roteador);
        o.RegistrarDevolucaoNaLoja([roteador.Id], Agora);
        var restantes = o.Equipamentos.Where(e => e.Id != roteador.Id).ToDictionary(e => e.Id, e => e.NumeroSerie);

        // O item devolvido não pode entrar na retirada...
        var comDevolvido = new Dictionary<Guid, NumeroSerie?>(restantes) { [roteador.Id] = roteador.NumeroSerie };
        DeveViolar(() => o.RegistrarRetirada(comDevolvido, NoCliente, Agora, FotoDeTeste.DoConjunto), "RF12", "não está mais com o cliente");

        // ...nem é cobrado nela.
        o.RegistrarRetirada(restantes, NoCliente, Agora, FotoDeTeste.DoConjunto);
        o.Status.ShouldBe(StatusOrdem.Concluida);
        roteador.Situacao.ShouldBe(SituacaoEquipamento.DevolvidoNaLoja);

        // A conferência também olha só o que veio da retirada.
        o.Conferir(restantes, Guid.NewGuid(), Agora).Fechou.ShouldBeTrue();
        roteador.Situacao.ShouldBe(SituacaoEquipamento.DevolvidoNaLoja);
    }

    [Fact]
    public void Recusa_lista_vazia_item_de_outra_ordem_e_item_ja_devolvido()
    {
        var o = ComTecnico();
        var roteador = Item(o, TipoEquipamento.Roteador).Id;

        DeveViolar(() => o.RegistrarDevolucaoNaLoja([], Agora), "RF15", "Escolha os itens");
        DeveViolar(() => o.RegistrarDevolucaoNaLoja([Guid.NewGuid()], Agora), "RF15", "não está mais com o cliente");
        o.RegistrarDevolucaoNaLoja([roteador], Agora);
        DeveViolar(() => o.RegistrarDevolucaoNaLoja([roteador], Agora), "RF15", "não está mais com o cliente");
    }

    [Fact]
    public void Item_ja_retirado_pelo_tecnico_nao_volta_pela_loja()
    {
        var o = ComTecnico();
        o.RegistrarRetirada(o.Equipamentos.ToDictionary(e => e.Id, e => e.NumeroSerie), NoCliente, Agora, FotoDeTeste.DoConjunto);
        DeveViolar(() => o.RegistrarDevolucaoNaLoja([Item(o, TipoEquipamento.ONU).Id], Agora), "RF15", "não está mais com o cliente");
    }

    [Fact]
    public void Devolucao_barrada_nao_muda_nada()
    {
        var o = ComTecnico();
        var versao = o.Versao;
        Should.Throw<RegraDeNegocioException>(() => o.RegistrarDevolucaoNaLoja([Item(o, TipoEquipamento.ONU).Id, Guid.NewGuid()], Agora));
        o.Versao.ShouldBe(versao);
        o.Equipamentos.ShouldAllBe(e => e.Situacao == SituacaoEquipamento.Pendente);
    }
}
