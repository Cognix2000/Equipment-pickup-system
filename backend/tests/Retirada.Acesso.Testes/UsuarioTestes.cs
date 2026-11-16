using Microsoft.AspNetCore.DataProtection;
using OtpNet;
using Retirada.Acesso.Dominio;
using Retirada.Acesso.Seguranca;
using Retirada.Comum;
using Shouldly;

namespace Retirada.Acesso.Testes;

public class UsuarioTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Gestor = Guid.NewGuid();

    private static Usuario Ativo(string papel = Papeis.Tecnico)
    {
        var u = Usuario.Cadastrar("Técnico Teste", " Tecnico.Teste@Exemplo.Invalid ", papel, Gestor, Agora);
        u.DefinirSenha("hash", Agora);
        return u;
    }

    private static RegraDeNegocioException DeveViolar(Action acao, string codigo)
    {
        var erro = Should.Throw<RegraDeNegocioException>(acao);
        erro.Codigo.ShouldBe(codigo);
        erro.Message.ShouldNotBeNullOrWhiteSpace();
        return erro;
    }

    [Fact]
    public void Cadastro_fica_pendente_com_email_normalizado() // RF02
    {
        var u = Usuario.Cadastrar("Ana", " Ana@Exemplo.Invalid ", Papeis.Financeiro, Gestor, Agora);

        u.Situacao.ShouldBe(SituacaoUsuario.Pendente);
        u.Email.ShouldBe("ana@exemplo.invalid");
        u.PodeEntrar(Agora).ShouldBeFalse();
        u.AprovadoPor.ShouldBe(Gestor);
    }

    [Theory]
    [InlineData("", "a@b.invalid", Papeis.Tecnico, "USUARIO_NOME")]
    [InlineData("Ana", "sem-arroba", Papeis.Tecnico, "USUARIO_EMAIL")]
    [InlineData("Ana", "a@b.invalid", Papeis.Parceiro, "USUARIO_PAPEL")]
    [InlineData("Ana", "a@b.invalid", "chefe", "USUARIO_PAPEL")]
    public void Cadastro_invalido_e_recusado(string nome, string email, string papel, string codigo) =>
        DeveViolar(() => Usuario.Cadastrar(nome, email, papel, Gestor, Agora), codigo);

    [Fact]
    public void Primeiro_gestor_aprova_a_si_mesmo()
    {
        var g = Usuario.CadastrarPrimeiroGestor("Gestor", "gestor@exemplo.invalid", Agora);
        g.AprovadoPor.ShouldBe(g.Id);
        g.Papel.ShouldBe(Papeis.Gestor);
    }

    [Fact]
    public void Quinta_falha_bloqueia_por_15_minutos() // RNF03
    {
        var u = Ativo();
        for (var i = 0; i < 4; i++) u.RegistrarFalha(Agora);
        u.EstaBloqueado(Agora).ShouldBeFalse();

        u.RegistrarFalha(Agora);

        u.EstaBloqueado(Agora).ShouldBeTrue();
        u.PodeEntrar(Agora.AddMinutes(14)).ShouldBeFalse();
        u.PodeEntrar(Agora.AddMinutes(15)).ShouldBeTrue();
        u.FalhasLogin.ShouldBe((short)0);
    }

    [Fact]
    public void Entrada_zera_as_falhas()
    {
        var u = Ativo();
        u.RegistrarFalha(Agora);
        u.RegistrarEntrada(Agora);
        u.FalhasLogin.ShouldBe((short)0);
        u.UltimoLoginEm.ShouldBe(Agora);
    }

    [Fact]
    public void Revogar_corta_o_acesso_e_troca_o_carimbo() // RF02
    {
        var u = Ativo();
        var carimbo = u.CarimboSeguranca;

        u.Revogar(Gestor, Agora);

        u.Situacao.ShouldBe(SituacaoUsuario.Revogado);
        u.CarimboSeguranca.ShouldNotBe(carimbo);
        u.PodeEntrar(Agora).ShouldBeFalse();
        DeveViolar(() => u.DefinirSenha("outra", Agora), "ACESSO_REVOGADO");
    }

    [Fact]
    public void Ninguem_revoga_a_si_mesmo()
    {
        var u = Ativo(Papeis.Gestor);
        DeveViolar(() => u.Revogar(u.Id, Agora), "USUARIO_AUTORREVOGAR");
    }

    [Fact]
    public void Reativar_volta_para_ativo_ou_pendente()
    {
        var comSenha = Ativo();
        comSenha.Revogar(Gestor, Agora);
        comSenha.Reativar(Agora);
        comSenha.Situacao.ShouldBe(SituacaoUsuario.Ativo);

        var semSenha = Usuario.Cadastrar("Ana", "ana@exemplo.invalid", Papeis.Estoque, Gestor, Agora);
        semSenha.Revogar(Gestor, Agora);
        semSenha.Reativar(Agora);
        semSenha.Situacao.ShouldBe(SituacaoUsuario.Pendente);

        DeveViolar(() => Ativo().Reativar(Agora), "USUARIO_NAO_REVOGADO");
    }

    [Theory]
    [InlineData(Papeis.Gestor)]
    [InlineData(Papeis.Financeiro)]
    [InlineData(Papeis.Tecnico)]
    [InlineData(Papeis.Estoque)]
    public void Usuario_recem_criado_nao_tem_2FA_configurado(string papel) => // RNF03 (nov/2026)
        Ativo(papel).Tem2FA.ShouldBeFalse();

    [Fact]
    public void Codigo_do_2FA_nao_vale_duas_vezes()
    {
        var u = Ativo(Papeis.Financeiro);
        u.PrepararTotp("cifrado", Agora);
        u.AtivarTotp(100, Agora);

        u.AceitarPassoTotp(100, Agora).ShouldBeFalse();
        u.AceitarPassoTotp(99, Agora).ShouldBeFalse();
        u.AceitarPassoTotp(101, Agora).ShouldBeTrue();
        u.AceitarPassoTotp(101, Agora).ShouldBeFalse();
    }

    [Fact]
    public void Zerar_2FA_obriga_configurar_de_novo()
    {
        var u = Ativo(Papeis.Financeiro);
        u.PrepararTotp("cifrado", Agora);
        u.AtivarTotp(100, Agora);
        DeveViolar(() => u.PrepararTotp("outro", Agora), "2FA_JA_ATIVO");
        var carimbo = u.CarimboSeguranca;

        u.Zerar2FA(Agora);

        u.Tem2FA.ShouldBeFalse();
        u.TotpSegredoCifrado.ShouldBeNull();
        u.CarimboSeguranca.ShouldNotBe(carimbo);
    }

    [Fact]
    public void Ativar_2FA_exige_segredo_preparado() =>
        DeveViolar(() => Ativo(Papeis.Financeiro).AtivarTotp(1, Agora), "2FA_SEM_SEGREDO");

    [Fact]
    public void Tecnico_entra_como_clt_e_o_gestor_muda_para_terceiro_so_no_tecnico()
    {
        Usuario.Cadastrar("Técnico", "t@exemplo.invalid", Papeis.Tecnico, Gestor, Agora).Vinculo.ShouldBe(Vinculos.Clt);
        Usuario.Cadastrar("Técnico", "t@exemplo.invalid", Papeis.Tecnico, Gestor, Agora, Vinculos.Terceiro).Vinculo.ShouldBe(Vinculos.Terceiro);
        Usuario.Cadastrar("Estoque", "e@exemplo.invalid", Papeis.Estoque, Gestor, Agora).Vinculo.ShouldBeNull();

        DeveViolar(() => Usuario.Cadastrar("Estoque", "e@exemplo.invalid", Papeis.Estoque, Gestor, Agora, Vinculos.Clt), "USUARIO_VINCULO");
        DeveViolar(() => Usuario.Cadastrar("Técnico", "t@exemplo.invalid", Papeis.Tecnico, Gestor, Agora, "pj"), "USUARIO_VINCULO");

        var tecnico = Ativo();
        tecnico.DefinirVinculo(Vinculos.Terceiro, Agora.AddDays(1));
        tecnico.Vinculo.ShouldBe(Vinculos.Terceiro);
        tecnico.AtualizadoEm.ShouldBe(Agora.AddDays(1));
        DeveViolar(() => Ativo(Papeis.Financeiro).DefinirVinculo(Vinculos.Terceiro, Agora), "USUARIO_VINCULO");
    }
}

public class ConviteTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Banco_guarda_so_o_hash_do_token()
    {
        var (convite, token) = Convite.Gerar(Guid.NewGuid(), Guid.NewGuid(), Agora);

        convite.TokenHash.ShouldNotContain(token);
        convite.TokenHash.ShouldBe(Convite.Hash(token));
        convite.TokenHash.Length.ShouldBe(64);
        token.Length.ShouldBeGreaterThanOrEqualTo(40);
    }

    [Fact]
    public void Convite_vale_48h_e_uma_vez_so()
    {
        var (convite, _) = Convite.Gerar(Guid.NewGuid(), Guid.NewGuid(), Agora);
        Should.Throw<RegraDeNegocioException>(() => convite.Usar(Agora.AddHours(48))).Codigo.ShouldBe("CONVITE_EXPIRADO");

        convite.Usar(Agora.AddHours(47));
        Should.Throw<RegraDeNegocioException>(() => convite.Usar(Agora.AddHours(47))).Codigo.ShouldBe("CONVITE_USADO");
    }
}

public class HashDeSenhaTestes
{
    [Fact]
    public void Gera_Argon2id_no_formato_PHC_e_confere()
    {
        var hash = HashDeSenha.Gerar("uma senha bem comprida");

        hash.ShouldStartWith("$argon2id$v=19$m=19456,t=2,p=1$");
        HashDeSenha.Verificar("uma senha bem comprida", hash).ShouldBeTrue();
        HashDeSenha.Verificar("uma senha bem comprid", hash).ShouldBeFalse();
        HashDeSenha.Verificar("qualquer", null).ShouldBeFalse();
        HashDeSenha.Verificar("qualquer", "lixo").ShouldBeFalse();
    }

    [Fact]
    public void Mesma_senha_gera_hash_diferente_por_causa_do_sal() =>
        HashDeSenha.Gerar("uma senha bem comprida").ShouldNotBe(HashDeSenha.Gerar("uma senha bem comprida"));

    [Theory]
    [InlineData("curta", "SENHA_CURTA")]
    [InlineData("tecnico.teste123456", "SENHA_COM_EMAIL")]
    public void Politica_de_senha(string senha, string codigo) =>
        Should.Throw<RegraDeNegocioException>(() => HashDeSenha.ValidarPolitica(senha, "tecnico.teste@exemplo.invalid")).Codigo.ShouldBe(codigo);

    [Fact]
    public void Senha_longa_demais_e_recusada() =>
        Should.Throw<RegraDeNegocioException>(() => HashDeSenha.ValidarPolitica(new string('a', 129), "x@y.invalid")).Codigo.ShouldBe("SENHA_LONGA");
}

public class SegundoFatorTestes
{
    private readonly SegundoFator _sf = new(new EphemeralDataProtectionProvider());

    [Fact]
    public void Codigo_do_aplicativo_confere_e_segredo_fica_cifrado()
    {
        var (base32, cifrado) = _sf.NovoSegredo();
        var agora = DateTimeOffset.UtcNow;
        var codigo = new Totp(Base32Encoding.ToBytes(base32)).ComputeTotp(agora.UtcDateTime);

        cifrado.ShouldNotContain(base32);
        _sf.Verificar(cifrado, codigo, agora).ShouldNotBeNull();
        _sf.Verificar(cifrado, codigo, agora.AddMinutes(5)).ShouldBeNull();
        _sf.Verificar(cifrado, "12345", agora).ShouldBeNull();
        _sf.Verificar(cifrado, null, agora).ShouldBeNull();
    }

    [Fact]
    public void Endereco_otpauth_tem_emissor_e_email() =>
        SegundoFator.EnderecoOtpAuth("ana@exemplo.invalid", "ABC").ShouldBe(
            "otpauth://totp/Retirada%20Netwise:ana%40exemplo.invalid?secret=ABC&issuer=Retirada%20Netwise&digits=6&period=30");
}
