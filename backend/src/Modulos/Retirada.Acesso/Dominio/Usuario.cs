using Retirada.Comum;

namespace Retirada.Acesso.Dominio;

public enum SituacaoUsuario { Pendente, Ativo, Suspenso, Revogado }

/// <summary>
/// Quem entra no sistema. O carimbo de segurança muda sempre que o acesso precisa cair na hora
/// (revogar, trocar senha, zerar 2FA): toda sessão aberta com o carimbo antigo é recusada (RF02).
/// </summary>
public sealed class Usuario : Entidade
{
    public const int LimiteDeFalhas = 5;
    public static readonly TimeSpan TempoDeBloqueio = TimeSpan.FromMinutes(15);

    private static readonly string[] PapeisDoIncremento1 = [Papeis.Gestor, Papeis.Financeiro, Papeis.Tecnico, Papeis.Estoque];

    // Stryker disable once String : valores iniciais só para o EF Core; a fábrica sempre preenche
    public string Nome { get; private set; } = string.Empty;
    // Stryker disable once String : idem
    public string Email { get; private set; } = string.Empty;
    // Stryker disable once String : idem
    public string Papel { get; private set; } = string.Empty;
    /// <summary>Só do técnico: CLT (recebe na folha) ou terceiro (resgata por PIX). Nos outros papéis, nulo.</summary>
    public string? Vinculo { get; private set; }
    public SituacaoUsuario Situacao { get; private set; }
    public string? SenhaHash { get; private set; }
    public short FalhasLogin { get; private set; }
    public DateTimeOffset? BloqueadoAte { get; private set; }
    public string? TotpSegredoCifrado { get; private set; }
    public DateTimeOffset? TotpAtivadoEm { get; private set; }
    public long? TotpUltimoPasso { get; private set; }
    public Guid CarimboSeguranca { get; private set; } = Guid.NewGuid();
    public DateTimeOffset? UltimoLoginEm { get; private set; }
    public Guid? AprovadoPor { get; private set; }
    public DateTimeOffset? AprovadoEm { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }
    public DateTimeOffset AtualizadoEm { get; private set; }

    /// <summary>RNF03: gestor e financeiro só entram com o segundo fator.</summary>
    public bool Exige2FA => Papel is Papeis.Gestor or Papeis.Financeiro;

    public bool Tem2FA => TotpAtivadoEm is not null;

    private Usuario() { } // EF Core

    /// <summary>RF02: o gestor cadastra e o usuário fica pendente até definir a senha pelo convite.</summary>
    public static Usuario Cadastrar(string nome, string email, string papel, Guid cadastradoPor, DateTimeOffset agora, string? vinculo = null)
    {
        if (string.IsNullOrWhiteSpace(nome))
            throw new RegraDeNegocioException("USUARIO_NOME", "Informe o nome.");
        var emailNormalizado = NormalizarEmail(email);
        if (!emailNormalizado.Contains('@') || emailNormalizado.Length > 254)
            throw new RegraDeNegocioException("USUARIO_EMAIL", "E-mail inválido.");
        if (!PapeisDoIncremento1.Contains(papel))
            throw new RegraDeNegocioException("USUARIO_PAPEL", "Papel inválido. Parceiros entram no incremento 3.");

        return new Usuario
        {
            Nome = nome.Trim(),
            Email = emailNormalizado,
            Papel = papel,
            Vinculo = VinculoDoPapel(papel, vinculo),
            Situacao = SituacaoUsuario.Pendente,
            AprovadoPor = cadastradoPor,
            AprovadoEm = agora,
            CriadoEm = agora,
            AtualizadoEm = agora,
        };
    }

    /// <summary>O gestor muda o vínculo do técnico (ex.: o terceiro foi contratado CLT). Vale para os próximos resgates.</summary>
    public void DefinirVinculo(string vinculo, DateTimeOffset agora)
    {
        if (Papel != Papeis.Tecnico)
            throw new RegraDeNegocioException("USUARIO_VINCULO", "Só o técnico tem vínculo (CLT ou terceiro).");
        Vinculo = VinculoDoPapel(Papel, vinculo);
        AtualizadoEm = agora;
    }

    /// <summary>Técnico sem vínculo informado entra como CLT, o caminho sem resgate por PIX.</summary>
    private static string? VinculoDoPapel(string papel, string? vinculo)
    {
        if (papel != Papeis.Tecnico)
            return vinculo is null ? null : throw new RegraDeNegocioException("USUARIO_VINCULO", "Só o técnico tem vínculo (CLT ou terceiro).");
        if (vinculo is null) return Vinculos.Clt;
        return Vinculos.Valido(vinculo) ? vinculo : throw new RegraDeNegocioException("USUARIO_VINCULO", "Vínculo inválido: use CLT ou terceiro.");
    }

    /// <summary>Só para instalar o sistema: o primeiro gestor aprova a si mesmo.</summary>
    public static Usuario CadastrarPrimeiroGestor(string nome, string email, DateTimeOffset agora) =>
        CadastrarAutoaprovado(nome, email, Papeis.Gestor, agora);

    internal static Usuario CadastrarAutoaprovado(string nome, string email, string papel, DateTimeOffset agora)
    {
        var usuario = Cadastrar(nome, email, papel, Guid.Empty, agora);
        usuario.AprovadoPor = usuario.Id;
        return usuario;
    }

    public static string NormalizarEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Primeiro acesso ou nova senha pelo convite: ativa e derruba sessões antigas.</summary>
    public void DefinirSenha(string senhaHash, DateTimeOffset agora)
    {
        if (Situacao == SituacaoUsuario.Revogado)
            throw new RegraDeNegocioException("ACESSO_REVOGADO", "Acesso revogado. Fale com o gestor.");
        SenhaHash = senhaHash;
        Situacao = SituacaoUsuario.Ativo;
        FalhasLogin = 0;
        BloqueadoAte = null;
        Renovar(agora);
    }

    public bool EstaBloqueado(DateTimeOffset agora) => BloqueadoAte is { } ate && ate > agora;

    public bool PodeEntrar(DateTimeOffset agora) =>
        Situacao == SituacaoUsuario.Ativo && SenhaHash is not null && !EstaBloqueado(agora);

    /// <summary>
    /// Com a chave de acesso, o bloqueio por senha errada não vale: ele existe contra quem tenta adivinhar a senha,
    /// e a chave não se adivinha. Assim, um ataque à senha não tranca para fora quem tem a chave.
    /// </summary>
    public bool PodeEntrarComChave => Situacao == SituacaoUsuario.Ativo;

    /// <summary>RNF03: na quinta falha seguida, bloqueia por 15 minutos.</summary>
    public void RegistrarFalha(DateTimeOffset agora)
    {
        FalhasLogin++;
        if (FalhasLogin >= LimiteDeFalhas)
        {
            BloqueadoAte = agora + TempoDeBloqueio;
            FalhasLogin = 0;
        }
        AtualizadoEm = agora;
    }

    public void RegistrarEntrada(DateTimeOffset agora)
    {
        FalhasLogin = 0;
        BloqueadoAte = null;
        UltimoLoginEm = agora;
        AtualizadoEm = agora;
    }

    /// <summary>Guarda o segredo novo até a pessoa provar, com um código, que configurou o aplicativo.</summary>
    public void PrepararTotp(string segredoCifrado, DateTimeOffset agora)
    {
        if (Tem2FA)
            throw new RegraDeNegocioException("2FA_JA_ATIVO", "O 2FA já está ativo. Peça ao gestor para zerar, se trocou de celular.");
        TotpSegredoCifrado = segredoCifrado;
        AtualizadoEm = agora;
    }

    public void AtivarTotp(long passo, DateTimeOffset agora)
    {
        if (TotpSegredoCifrado is null)
            throw new RegraDeNegocioException("2FA_SEM_SEGREDO", "Comece a configuração do 2FA de novo.");
        TotpAtivadoEm = agora;
        TotpUltimoPasso = passo;
        AtualizadoEm = agora;
    }

    /// <summary>Um código vale uma vez só: reusar o mesmo código é recusado.</summary>
    public bool AceitarPassoTotp(long passo, DateTimeOffset agora)
    {
        if (TotpUltimoPasso is { } ultimo && passo <= ultimo) return false;
        TotpUltimoPasso = passo;
        AtualizadoEm = agora;
        return true;
    }

    /// <summary>Gestor zera o 2FA de quem trocou de celular; a pessoa configura de novo no próximo login.</summary>
    public void Zerar2FA(DateTimeOffset agora)
    {
        TotpSegredoCifrado = null;
        TotpAtivadoEm = null;
        TotpUltimoPasso = null;
        Renovar(agora);
    }

    /// <summary>RF02: revogar corta o acesso na hora.</summary>
    public void Revogar(Guid porQuem, DateTimeOffset agora)
    {
        if (porQuem == Id)
            throw new RegraDeNegocioException("USUARIO_AUTORREVOGAR", "Você não pode revogar o próprio acesso.");
        Situacao = SituacaoUsuario.Revogado;
        Renovar(agora);
    }

    public void Reativar(DateTimeOffset agora)
    {
        if (Situacao != SituacaoUsuario.Revogado)
            throw new RegraDeNegocioException("USUARIO_NAO_REVOGADO", "Só usuário revogado pode ser reativado.");
        Situacao = SenhaHash is null ? SituacaoUsuario.Pendente : SituacaoUsuario.Ativo;
        Renovar(agora);
    }

    private void Renovar(DateTimeOffset agora)
    {
        CarimboSeguranca = Guid.NewGuid();
        AtualizadoEm = agora;
    }
}
