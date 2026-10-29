using Retirada.Acesso.Dominio;
using Retirada.Comum;
using Shouldly;

namespace Retirada.Acesso.Testes;

public class ChaveDeAcessoTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Pessoa = Guid.NewGuid();

    [Fact]
    public void Sem_nome_vira_chave_de_acesso_e_nome_longo_e_cortado()
    {
        ChaveDeAcesso.Cadastrar(Pessoa, [1], [2], 0, "  ", Agora).Nome.ShouldBe("Chave de acesso");
        ChaveDeAcesso.Cadastrar(Pessoa, [1], [2], 0, "  Celular  ", Agora).Nome.ShouldBe("Celular");
        ChaveDeAcesso.Cadastrar(Pessoa, [1], [2], 0, new string('x', 80), Agora).Nome.Length.ShouldBe(ChaveDeAcesso.TamanhoMaximoDoNome);
    }

    [Fact]
    public void Sem_credencial_ou_chave_publica_nao_cadastra()
    {
        Should.Throw<RegraDeNegocioException>(() => ChaveDeAcesso.Cadastrar(Pessoa, [], [2], 0, null, Agora)).Codigo.ShouldBe("CHAVE_INVALIDA");
        Should.Throw<RegraDeNegocioException>(() => ChaveDeAcesso.Cadastrar(Pessoa, [1], [], 0, null, Agora)).Codigo.ShouldBe("CHAVE_INVALIDA");
    }

    [Fact]
    public void Usar_guarda_o_contador_e_a_hora()
    {
        var chave = ChaveDeAcesso.Cadastrar(Pessoa, [1], [2], 3, "Celular", Agora);

        chave.RegistrarUso(7, Agora.AddDays(1));

        chave.Contador.ShouldBe(7);
        chave.UltimoUsoEm.ShouldBe(Agora.AddDays(1));
    }

    [Fact]
    public void Bloqueio_por_senha_errada_nao_tranca_quem_entra_com_a_chave()
    {
        var u = Usuario.Cadastrar("Financeiro Teste", "financeiro.teste@exemplo.invalid", Papeis.Financeiro, Guid.NewGuid(), Agora);
        u.PodeEntrarComChave.ShouldBeFalse(); // pendente
        u.DefinirSenha("hash", Agora);
        for (var i = 0; i < Usuario.LimiteDeFalhas; i++) u.RegistrarFalha(Agora);

        u.PodeEntrar(Agora).ShouldBeFalse();
        u.PodeEntrarComChave.ShouldBeTrue();

        u.Revogar(Guid.NewGuid(), Agora);
        u.PodeEntrarComChave.ShouldBeFalse();
    }
}
