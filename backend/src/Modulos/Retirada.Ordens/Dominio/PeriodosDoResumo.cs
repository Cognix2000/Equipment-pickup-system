namespace Retirada.Ordens.Dominio;

/// <summary>
/// Onde começam hoje, a semana (segunda-feira) e o mês no horário de Brasília, para o resumo do técnico.
/// O fuso é fixo, como em <see cref="JanelaDeContato"/>: sem horário de verão desde 2019 e sem depender do tzdata da imagem.
/// </summary>
public sealed record PeriodosDoResumo(DateTimeOffset Hoje, DateTimeOffset Semana, DateTimeOffset Mes)
{
    public static readonly TimeSpan Brasilia = TimeSpan.FromHours(-3);

    public static PeriodosDoResumo De(DateTimeOffset agora)
    {
        var local = agora.ToOffset(Brasilia);
        var hoje = new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, Brasilia);
        var diasDesdeSegunda = ((int)local.DayOfWeek + 6) % 7; // domingo = 6
        return new PeriodosDoResumo(hoje, hoje.AddDays(-diasDesdeSegunda), new DateTimeOffset(local.Year, local.Month, 1, 0, 0, 0, Brasilia));
    }

    /// <summary>O mais antigo dos três: a semana pode começar no mês anterior.</summary>
    public DateTimeOffset Inicio => Semana < Mes ? Semana : Mes;
}
