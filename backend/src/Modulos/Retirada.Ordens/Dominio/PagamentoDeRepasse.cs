using Retirada.Comum;

namespace Retirada.Ordens.Dominio;

/// <summary>
/// Repasse ao técnico, como o do entregador de aplicativo: cada retirada conferida pelo estoque vira um crédito,
/// e o financeiro registra de uma vez o pagamento de tudo o que está em aberto. Técnico CLT ou terceiro recebe igual:
/// o CLT na folha (o financeiro registra aqui) e o terceiro pelo resgate por PIX (ADR-014, <see cref="RegrasDoResgate"/>).
/// </summary>
public static class PagamentoDeRepasse
{
    public const int TamanhoMaximoDaObservacao = 200;

    /// <summary>
    /// O financeiro confirma o valor que viu na tela. Se entrou crédito novo nesse meio-tempo (o estoque conferiu
    /// outra retirada), o pagamento é recusado para ele ver o valor certo antes de registrar.
    /// </summary>
    public static void Validar(int valorConfirmado, int valorEmAberto, int quantidadeEmAberto, string? observacao)
    {
        if (quantidadeEmAberto == 0 || valorEmAberto <= 0)
            throw new RegraDeNegocioException("REPASSE_SEM_SALDO", "Não há retirada conferida esperando pagamento para este técnico.");
        if (valorConfirmado != valorEmAberto)
            throw new RegraDeNegocioException("REPASSE_VALOR_MUDOU", "O valor a pagar mudou: o estoque conferiu outra retirada. Atualize a tela e confira de novo.");
        if (observacao is { Length: > TamanhoMaximoDaObservacao })
            throw new RegraDeNegocioException("REPASSE_OBSERVACAO", $"A observação passa de {TamanhoMaximoDaObservacao} caracteres.");
    }
}
