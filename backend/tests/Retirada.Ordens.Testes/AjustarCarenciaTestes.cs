using Retirada.Comum;
using Retirada.Ordens.Dominio;
using Shouldly;

namespace Retirada.Ordens.Testes;

/// <summary>RN03 ajustável: o financeiro muda a carência caso a caso.</summary>
public class AjustarCarenciaTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly Endereco Rua = new("RUA DOS IPÊS", "120", null, "CENTRO", "CIDADE SIMULADA", "MG", null);

    private static Ordem Nova(TimeSpan? carencia = null) => Ordem.Importar(
        "os:1", null, "REMOÇÃO EQUIP FTTH", Guid.NewGuid(), Rua, null, null, [], Agora, carencia ?? TimeSpan.FromHours(48),
        new ReferenciasDaImportacao(IdOrdemServico: 1));

    [Fact]
    public void Prazo_no_futuro_altera_a_data_mas_mantem_em_carencia()
    {
        var ordem = Nova();
        var depois = Agora.AddDays(5);
        ordem.AjustarCarencia(depois, Agora);
        ordem.Status.ShouldBe(StatusOrdem.Nova);
        ordem.CarenciaAte.ShouldBe(depois);
        ordem.Eventos.Select(e => e.Tipo).ShouldBe(["importada", "carencia_ajustada"]);
    }

    [Fact]
    public void Prazo_agora_ou_no_passado_libera_a_ordem_na_hora()
    {
        var ordem = Nova();
        ordem.AjustarCarencia(Agora, Agora);
        ordem.Status.ShouldBe(StatusOrdem.Disponivel);
        ordem.Eventos[^1].Tipo.ShouldBe("liberada");
    }

    [Fact]
    public void So_ordem_em_carencia_aceita_ajuste()
    {
        var ordem = Nova();
        ordem.Liberar(Agora);
        Should.Throw<RegraDeNegocioException>(() => ordem.AjustarCarencia(Agora.AddDays(2), Agora)).Codigo.ShouldBe("RN03");
    }

    [Fact]
    public void Prazo_absurdamente_longe_e_recusado()
    {
        var ordem = Nova();
        Should.Throw<RegraDeNegocioException>(() => ordem.AjustarCarencia(Agora.AddYears(5), Agora)).Codigo.ShouldBe("RN03");
    }
}
