namespace Retirada.Ordens.Dominio;

/// <summary>
/// RN10: contato com o cliente só de segunda a sábado, das 8h às 18h, no horário de Brasília
/// (parâmetro horario_contato; validar com o jurídico). Fora disso o registro é aceito, mas fica marcado
/// para o gestor ver: o técnico pode anotar às 18h05 a ligação que fez às 17h59.
/// </summary>
public static class JanelaDeContato
{
    /// <summary>Brasília sem horário de verão desde 2019. Fixo, para não depender do tzdata da imagem.</summary>
    private static readonly TimeSpan Brasilia = TimeSpan.FromHours(-3);

    public static bool Permite(DateTimeOffset instante)
    {
        var local = instante.ToOffset(Brasilia);
        return local.DayOfWeek != DayOfWeek.Sunday && local.Hour is >= 8 and < 18;
    }
}
