using Retirada.Comum;
using Retirada.Ordens.Dominio;
using Shouldly;

namespace Retirada.Ordens.Testes;

public class PagamentoDeRepasseTestes
{
    private static string Codigo(Action acao) => Should.Throw<RegraDeNegocioException>(acao).Codigo;

    [Fact]
    public void Paga_quando_o_valor_confirmado_e_o_que_esta_em_aberto()
    {
        Should.NotThrow(() => PagamentoDeRepasse.Validar(1500, 1500, 3, "PIX de sexta"));
        Should.NotThrow(() => PagamentoDeRepasse.Validar(500, 500, 1, null));
    }

    [Fact]
    public void Sem_saldo_nao_registra_pagamento()
    {
        Codigo(() => PagamentoDeRepasse.Validar(0, 0, 0, null)).ShouldBe("REPASSE_SEM_SALDO");
    }

    [Fact]
    public void Valor_diferente_do_que_esta_em_aberto_e_recusado() // entrou crédito novo depois que a tela abriu
    {
        Codigo(() => PagamentoDeRepasse.Validar(1000, 1500, 3, null)).ShouldBe("REPASSE_VALOR_MUDOU");
    }

    [Fact]
    public void Observacao_longa_e_recusada()
    {
        Codigo(() => PagamentoDeRepasse.Validar(500, 500, 1, new string('x', PagamentoDeRepasse.TamanhoMaximoDaObservacao + 1)))
            .ShouldBe("REPASSE_OBSERVACAO");
    }
}
