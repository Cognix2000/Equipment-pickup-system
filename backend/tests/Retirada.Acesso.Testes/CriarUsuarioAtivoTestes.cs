using Microsoft.EntityFrameworkCore;
using Retirada.Acesso.Dominio;
using Retirada.Acesso.Infraestrutura;
using Retirada.Acesso.Seguranca;
using Retirada.Comum;
using Shouldly;

namespace Retirada.Acesso.Testes;

/// <summary>Cadastro autoaprovado com senha já definida, para o começo da operação.</summary>
public class CadastroAutoaprovadoComVinculoTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Tecnico_recebe_o_vinculo_e_entra_ativo_quando_a_senha_esta_definida()
    {
        var u = Usuario.CadastrarAutoaprovadoComVinculo("Terceiro", "terceiro@exemplo.invalid", Papeis.Tecnico, Agora, Vinculos.Terceiro);
        u.Vinculo.ShouldBe(Vinculos.Terceiro);
        u.Papel.ShouldBe(Papeis.Tecnico);
        u.AprovadoPor.ShouldBe(u.Id);
        u.Situacao.ShouldBe(SituacaoUsuario.Pendente); // só vira ativo com a senha
        u.DefinirSenha(HashDeSenha.Gerar("senha-forte-de-testes"), Agora);
        u.Situacao.ShouldBe(SituacaoUsuario.Ativo);
        u.PodeEntrar(Agora).ShouldBeTrue();
    }

    [Fact]
    public void Papel_sem_vinculo_e_recusado_e_tecnico_sem_vinculo_entra_como_clt()
    {
        Should.Throw<RegraDeNegocioException>(() =>
            Usuario.CadastrarAutoaprovadoComVinculo("X", "x@exemplo.invalid", Papeis.Financeiro, Agora, Vinculos.Terceiro)).Codigo.ShouldBe("USUARIO_VINCULO");
        Usuario.CadastrarAutoaprovadoComVinculo("CLT", "clt@exemplo.invalid", Papeis.Tecnico, Agora, null).Vinculo.ShouldBe(Vinculos.Clt);
    }
}
