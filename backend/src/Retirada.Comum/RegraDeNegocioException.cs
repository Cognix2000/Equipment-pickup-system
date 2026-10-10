namespace Retirada.Comum;

/// <summary>
/// Uma regra de negócio impediu a operação. <see cref="Codigo"/> aponta a regra do documento
/// de requisitos (ex.: "RN07"), para a API devolver um erro que o front-end sabe explicar.
/// </summary>
public sealed class RegraDeNegocioException(string codigo, string mensagem) : Exception(mensagem)
{
    public string Codigo { get; } = codigo;
}
