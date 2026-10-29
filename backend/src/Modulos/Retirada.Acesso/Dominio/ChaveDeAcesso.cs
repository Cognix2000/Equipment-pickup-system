using Retirada.Comum;

namespace Retirada.Acesso.Dominio;

/// <summary>
/// Chave de acesso (passkey, WebAuthn): a digital, o rosto ou o PIN do aparelho no lugar da senha (ADR-012).
/// O servidor guarda só a chave pública; a privada nunca sai do aparelho (ou do cofre do Google/Apple/Microsoft).
/// Como o aparelho exige a digital ou o PIN (verificação do usuário), uma chave vale pelos dois fatores.
/// </summary>
public sealed class ChaveDeAcesso : Entidade
{
    public const int TamanhoMaximoDoNome = 60;

    public Guid UsuarioId { get; private set; }
    public byte[] CredencialId { get; private set; } = [];
    public byte[] ChavePublica { get; private set; } = [];
    public long Contador { get; private set; }
    // Stryker disable once String : valor inicial só para o EF Core; a fábrica sempre preenche
    public string Nome { get; private set; } = string.Empty;
    public DateTimeOffset CriadaEm { get; private set; }
    public DateTimeOffset? UltimoUsoEm { get; private set; }

    private ChaveDeAcesso() { } // EF Core

    public static ChaveDeAcesso Cadastrar(Guid usuarioId, byte[] credencialId, byte[] chavePublica, uint contador, string? nome, DateTimeOffset agora)
    {
        if (credencialId.Length == 0 || chavePublica.Length == 0)
            throw new RegraDeNegocioException("CHAVE_INVALIDA", "O aparelho não devolveu a chave. Tente de novo.");

        var nomeLimpo = string.IsNullOrWhiteSpace(nome) ? "Chave de acesso" : nome.Trim();
        if (nomeLimpo.Length > TamanhoMaximoDoNome) nomeLimpo = nomeLimpo[..TamanhoMaximoDoNome];

        return new ChaveDeAcesso
        {
            UsuarioId = usuarioId,
            CredencialId = credencialId,
            ChavePublica = chavePublica,
            Contador = contador,
            Nome = nomeLimpo,
            CriadaEm = agora,
        };
    }

    /// <summary>
    /// Guarda o contador novo. Chave sincronizada (Google, Apple) manda sempre 0; quando o aparelho conta,
    /// a biblioteca já recusou contador que não subiu, sinal de chave clonada.
    /// </summary>
    public void RegistrarUso(uint contador, DateTimeOffset agora)
    {
        Contador = contador;
        UltimoUsoEm = agora;
    }
}
