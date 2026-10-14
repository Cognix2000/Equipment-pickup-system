using Retirada.Comum;
using Retirada.Ordens.Dominio;
using Shouldly;

namespace Retirada.Ordens.Testes;

/// <summary>RN01: reimportar não duplica, só atualiza o que mudou, e só em ordem que ainda não terminou.</summary>
public class AtualizacaoPelaImportacaoTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly Endereco Rua = new("RUA DOS IPÊS", "120", null, "CENTRO", "CIDADE SIMULADA", "MG", null);
    private static readonly DateTimeOffset Programada = new(2026, 9, 15, 9, 0, 0, TimeSpan.FromHours(-3));

    private static Ordem Gravada() => Ordem.Importar("os:1", 5101, "REMOÇÃO EQUIP FTTH", Guid.NewGuid(), Rua, new Localizacao(-20, -45), "LIGAR ANTES",
        [Equipamento.Criar(TipoEquipamento.Fonte, "Fonte 12V", null)], Agora, TimeSpan.FromHours(48),
        new ReferenciasDaImportacao(Protocolo: "20260914101500123456", IdOrdemServico: 1, DataProgramada: Programada, DataRescisao: new DateOnly(2026, 9, 14), Cep: "00000-050"));

    private static DadosDaImportacao Iguais(Ordem o) => new(o.Endereco, o.Cep, o.Observacao, o.NumeroOs, o.DataProgramada, o.DataRescisao, o.Localizacao);

    [Fact]
    public void Importar_guarda_as_referencias_da_exportacao()
    {
        var o = Gravada();
        o.ShouldSatisfyAllConditions(
            x => x.Protocolo.ShouldBe("20260914101500123456"),
            x => x.IdOrdemServicoHubsoft.ShouldBe(1),
            x => x.DataProgramada.ShouldBe(Programada),
            x => x.DataRescisao.ShouldBe(new DateOnly(2026, 9, 14)),
            x => x.Cep.ShouldBe("00000-050"));
    }

    [Fact]
    public void Sem_mudanca_nada_acontece()
    {
        var o = Gravada();
        var versao = o.Versao;

        o.AtualizarPelaImportacao(Iguais(o), Agora).ShouldBeEmpty();

        o.Versao.ShouldBe(versao);
        o.Eventos.Select(e => e.Tipo).ShouldBe(["importada"]);
    }

    public static TheoryData<string, Func<DadosDaImportacao, DadosDaImportacao>> CadaCampo => new()
    {
        { "endereço", d => d with { Endereco = d.Endereco with { Numero = "122" } } },
        { "CEP", d => d with { Cep = "00000-060" } },
        { "observação", d => d with { Observacao = "CLIENTE PEDIU À TARDE" } },
        { "nº da OS", d => d with { NumeroOs = 5199 } },
        { "data programada", d => d with { DataProgramada = Programada.AddDays(1) } },
        { "data da rescisão", d => d with { DataRescisao = new DateOnly(2026, 9, 15) } },
        { "localização", d => d with { Localizacao = new Localizacao(-21, -45) } },
    };

    [Theory, MemberData(nameof(CadaCampo))]
    public void Cada_campo_que_mudou_aparece_e_e_aplicado(string campo, Func<DadosDaImportacao, DadosDaImportacao> mudar)
    {
        var o = Gravada();
        var novos = mudar(Iguais(o));

        o.MudancasDaImportacao(novos).ShouldBe([campo]);
        o.AtualizarPelaImportacao(novos, Agora.AddHours(1)).ShouldBe([campo]);

        Iguais(o).ShouldBe(novos);
        o.Versao.ShouldBe(2);
        o.AtualizadoEm.ShouldBe(Agora.AddHours(1));
        o.Eventos[^1].Tipo.ShouldBe("atualizada_pela_importacao");
    }

    [Fact]
    public void Campo_que_a_planilha_nova_nao_traz_fica_como_estava()
    {
        var o = Gravada();

        o.AtualizarPelaImportacao(Iguais(o) with { Cep = null, NumeroOs = null, DataProgramada = null, DataRescisao = null, Localizacao = null }, Agora)
            .ShouldBeEmpty();

        o.ShouldSatisfyAllConditions(
            x => x.Cep.ShouldBe("00000-050"),
            x => x.NumeroOs.ShouldBe(5101),
            x => x.DataProgramada.ShouldBe(Programada),
            x => x.Localizacao.ShouldNotBeNull());
    }

    [Fact]
    public void Ordem_encerrada_nao_aceita_atualizacao()
    {
        var o = Gravada();
        o.Cancelar(MotivoCancelamento.Pagou, Agora);

        o.AceitaAtualizacaoDaImportacao.ShouldBeFalse();
        Should.Throw<RegraDeNegocioException>(() => o.AtualizarPelaImportacao(Iguais(o) with { Observacao = "X" }, Agora)).Codigo.ShouldBe("RN01");
    }

    [Fact]
    public void Ordem_em_campo_ainda_aceita_atualizacao()
    {
        var o = Gravada();
        o.Liberar(Agora);
        o.Designar(Guid.NewGuid(), Agora);

        o.AceitaAtualizacaoDaImportacao.ShouldBeTrue();
    }

    [Fact]
    public void Equipamento_guarda_MAC_e_valor_e_recusa_valor_negativo()
    {
        var onu = Equipamento.Criar(TipoEquipamento.ONU, "Modelo", NumeroSerie.Criar("SIMU00000001"), "02:1A:2B:3C:4D:5E", 120m);
        onu.Mac.ShouldBe("02:1A:2B:3C:4D:5E");
        onu.Valor.ShouldBe(120m);

        Should.Throw<RegraDeNegocioException>(() => Equipamento.Criar(TipoEquipamento.Fonte, "Fonte", null, valor: -1m)).Codigo.ShouldBe("VALOR");
    }
}
