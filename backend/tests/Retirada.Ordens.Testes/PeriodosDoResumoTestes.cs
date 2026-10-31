using Retirada.Ordens.Dominio;
using Shouldly;

namespace Retirada.Ordens.Testes;

public class PeriodosDoResumoTestes
{
    private static readonly TimeSpan Brasilia = TimeSpan.FromHours(-3);

    [Fact]
    public void Hoje_semana_e_mes_comecam_a_meia_noite_de_Brasilia()
    {
        // Quarta, 29/10/2026, 01:30 em UTC = terça, 28/10, 22:30 em Brasília.
        var p = PeriodosDoResumo.De(new DateTimeOffset(2026, 10, 29, 1, 30, 0, TimeSpan.Zero));

        p.Hoje.ShouldBe(new DateTimeOffset(2026, 10, 28, 0, 0, 0, Brasilia));
        p.Semana.ShouldBe(new DateTimeOffset(2026, 10, 26, 0, 0, 0, Brasilia)); // segunda-feira
        p.Mes.ShouldBe(new DateTimeOffset(2026, 10, 1, 0, 0, 0, Brasilia));
        p.Inicio.ShouldBe(p.Mes);
    }

    [Fact]
    public void Domingo_ainda_e_da_semana_que_comecou_na_segunda()
    {
        var p = PeriodosDoResumo.De(new DateTimeOffset(2026, 11, 1, 12, 0, 0, Brasilia)); // domingo, 01/11

        p.Semana.ShouldBe(new DateTimeOffset(2026, 10, 26, 0, 0, 0, Brasilia));
        p.Mes.ShouldBe(new DateTimeOffset(2026, 11, 1, 0, 0, 0, Brasilia));
        p.Inicio.ShouldBe(p.Semana); // a semana começou no mês anterior
    }
}
