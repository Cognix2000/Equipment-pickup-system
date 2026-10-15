using Retirada.Comum;
using Retirada.Ordens.Dominio;
using Shouldly;

namespace Retirada.Ordens.Testes;

/// <summary>RN14: fotos da retirada como prova do que o técnico recolheu.</summary>
public class FotosDaRetiradaTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 15, 14, 0, 0, TimeSpan.Zero);
    private static readonly Localizacao NoCliente = new(-21.0005, -38.9990);

    private static Ordem ComTecnico()
    {
        var o = Ordem.Importar(
            "os-9501", 5501, "REMOÇÃO EQUIP FTTH", Guid.NewGuid(),
            new Endereco("RUA DOS IPÊS", "120", null, "CENTRO", "CIDADE SIMULADA", "MG", null),
            null, null,
            [
                Equipamento.Criar(TipoEquipamento.ONU, "Huawei EG8010H", NumeroSerie.Criar("HWTC5A5B5C5D")),
                Equipamento.Criar(TipoEquipamento.Fonte, "Fonte 12V", null),
            ],
            Agora, TimeSpan.FromHours(48));
        o.Liberar(Agora);
        o.Designar(Guid.NewGuid(), Agora);
        return o;
    }

    private static Equipamento Onu(Ordem o) => o.Equipamentos.Single(e => e.Tipo == TipoEquipamento.ONU);
    private static Equipamento Fonte(Ordem o) => o.Equipamentos.Single(e => e.Tipo == TipoEquipamento.Fonte);

    private static Dictionary<Guid, NumeroSerie?> Corretas(Ordem o) => o.Equipamentos.ToDictionary(e => e.Id, e => e.NumeroSerie);

    private static Dictionary<Guid, NumeroSerie?> ComDivergencia(Ordem o) =>
        o.Equipamentos.ToDictionary(e => e.Id, e => e.EhPatrimonio ? NumeroSerie.Criar("OUTRASERIE01") : null);

    private static void DeveViolar(Ordem o, Dictionary<Guid, NumeroSerie?> series, FotoDaRetirada[] fotos, string trecho)
    {
        var erro = Should.Throw<RegraDeNegocioException>(() => o.RegistrarRetirada(series, NoCliente, Agora, fotos));
        erro.Codigo.ShouldBe("RN14");
        erro.Message.ShouldContain(trecho);
        o.Status.ShouldBe(StatusOrdem.Atribuida); // barrada antes de mudar qualquer coisa
        o.Equipamentos.ShouldAllBe(e => e.Situacao == SituacaoEquipamento.Pendente);
    }

    [Fact]
    public void Sem_foto_dos_equipamentos_nao_registra()
    {
        var o = ComTecnico();
        DeveViolar(o, Corretas(o), [], "foto dos equipamentos");
        DeveViolar(o, Corretas(o), [new(TipoDeFoto.EtiquetaSerie, Onu(o).Id)], "foto dos equipamentos");
    }

    [Fact]
    public void Uma_foto_dos_equipamentos_basta_quando_as_series_conferem()
    {
        var o = ComTecnico();
        o.RegistrarRetirada(Corretas(o), NoCliente, Agora, [new(TipoDeFoto.Equipamento)]);
        o.Status.ShouldBe(StatusOrdem.Concluida);
    }

    [Fact]
    public void Item_divergente_exige_foto_da_etiqueta_dele()
    {
        var o = ComTecnico();
        DeveViolar(o, ComDivergencia(o), [new(TipoDeFoto.Equipamento)], "Huawei EG8010H");

        o.RegistrarRetirada(ComDivergencia(o), NoCliente, Agora,
            [new(TipoDeFoto.Equipamento), new(TipoDeFoto.EtiquetaSerie, Onu(o).Id)]);
        Onu(o).Situacao.ShouldBe(SituacaoEquipamento.Divergente);
    }

    [Fact]
    public void Etiqueta_precisa_dizer_o_item_e_fonte_nao_tem_etiqueta()
    {
        var o = ComTecnico();
        DeveViolar(o, Corretas(o), [new(TipoDeFoto.Equipamento), new(TipoDeFoto.EtiquetaSerie)], "de qual item");
        DeveViolar(o, Corretas(o), [new(TipoDeFoto.Equipamento), new(TipoDeFoto.EtiquetaSerie, Fonte(o).Id)], "fonte não tem");
    }

    [Fact]
    public void Foto_de_item_de_outra_ordem_nao_vale()
    {
        var o = ComTecnico();
        DeveViolar(o, Corretas(o), [new(TipoDeFoto.Equipamento, Guid.NewGuid())], "não é desta ordem");
    }

    [Fact]
    public void Comprovante_da_loja_nao_e_foto_de_retirada()
    {
        var o = ComTecnico();
        DeveViolar(o, Corretas(o), [new(TipoDeFoto.Equipamento), new(TipoDeFoto.ComprovanteLoja)], "dos equipamentos e das etiquetas");
    }

    [Fact]
    public void No_maximo_dez_fotos()
    {
        var o = ComTecnico();
        var onze = Enumerable.Repeat(new FotoDaRetirada(TipoDeFoto.Equipamento), Ordem.LimiteDeFotos + 1).ToArray();
        DeveViolar(o, Corretas(o), onze, "no máximo 10");
        o.RegistrarRetirada(Corretas(o), NoCliente, Agora, onze[..Ordem.LimiteDeFotos]);
        o.Status.ShouldBe(StatusOrdem.Concluida);
    }
}
