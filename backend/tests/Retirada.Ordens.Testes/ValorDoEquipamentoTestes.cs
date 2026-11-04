using Retirada.Ordens.Importacao;
using Shouldly;

namespace Retirada.Ordens.Testes;

/// <summary>Valor do equipamento na planilha: célula numérica do Excel (ponto decimal) ou texto em reais (pt-BR).</summary>
public class ValorDoEquipamentoTestes
{
    [Theory]
    [InlineData("150.5", 150.5)]      // célula numérica: o leitor entrega com ponto decimal
    [InlineData("89.9", 89.9)]
    [InlineData("150", 150)]
    [InlineData("0.75", 0.75)]
    [InlineData("150,50", 150.5)]     // texto digitado
    [InlineData("R$ 1.500,00", 1500)]
    [InlineData("R$ 350,00", 350)] // espaço que o Excel põe depois do R$
    [InlineData("1.500", 1500)]       // ponto em grupos de três: milhar
    [InlineData("12.345.678", 12345678)]
    public void Le_o_valor_nos_dois_formatos(string texto, double esperado) =>
        Interpretador.Valor(texto).ShouldBe((decimal)esperado);

    [Theory]
    [InlineData("-10")]
    [InlineData("cento e cinquenta")]
    [InlineData("1,234.56")]
    public void Valor_negativo_ou_fora_do_padrao_fica_vazio(string texto) =>
        Interpretador.Valor(texto).ShouldBeNull();
}
