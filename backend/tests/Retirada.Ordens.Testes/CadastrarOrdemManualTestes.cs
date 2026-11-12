using Retirada.Comum;
using Retirada.Ordens.Dominio;
using Shouldly;

namespace Retirada.Ordens.Testes;

/// <summary>Cadastro manual pelo financeiro: nasce Nova e em carência, com evento próprio no histórico.</summary>
public class CadastrarOrdemManualTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly Endereco Rua = new("RUA DOS IPÊS", "120", null, "CENTRO", "CIDADE SIMULADA", "MG", null);
    private static readonly Equipamento Ont = Equipamento.Criar(
        TipoEquipamento.ONT, "HG8145V5", NumeroSerie.Criar("SIMU0001"), "aa:bb:cc:dd:ee:ff", 350m);

    [Fact]
    public void Nasce_nova_e_em_carencia_com_evento_proprio()
    {
        var ordem = Ordem.CriarManual(
            "manual:abc123", "RETIRADA DE EQUIPAMENTO", Guid.NewGuid(), Rua, "30123-456", "Falar com o porteiro", [Ont],
            Agora, TimeSpan.FromHours(48));

        ordem.Status.ShouldBe(StatusOrdem.Nova);
        ordem.CarenciaAte.ShouldBe(Agora.AddHours(48));
        ordem.Equipamentos.ShouldHaveSingleItem();
        ordem.ChaveImportacao.ShouldBe("manual:abc123");
        ordem.Cep.ShouldBe("30123-456");
        ordem.Observacao.ShouldBe("Falar com o porteiro");
        ordem.Eventos.ShouldHaveSingleItem().Tipo.ShouldBe("cadastrada_manualmente");
    }

    [Fact]
    public void Chave_vazia_recusa_RN01()
    {
        Should.Throw<RegraDeNegocioException>(() => Ordem.CriarManual(
                "", "RETIRADA DE EQUIPAMENTO", Guid.NewGuid(), Rua, null, null, [Ont], Agora, TimeSpan.FromHours(48)))
            .Codigo.ShouldBe("RN01");
    }

    [Fact]
    public void Tipo_vazio_recusa_RN02()
    {
        Should.Throw<RegraDeNegocioException>(() => Ordem.CriarManual(
                "manual:abc123", "   ", Guid.NewGuid(), Rua, null, null, [Ont], Agora, TimeSpan.FromHours(48)))
            .Codigo.ShouldBe("RN02");
    }

    [Fact]
    public void Nao_usa_evento_de_importacao()
    {
        // Para o histórico não confundir com RF04 ("importada da planilha").
        var ordem = Ordem.CriarManual(
            "manual:abc123", "RETIRADA DE EQUIPAMENTO", Guid.NewGuid(), Rua, null, null, [Ont], Agora, TimeSpan.FromHours(48));
        ordem.Eventos[0].Tipo.ShouldNotBe("importada");
    }
}
