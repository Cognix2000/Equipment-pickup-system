namespace Retirada.Acesso.Dominio;

/// <summary>
/// Inscrição de Web Push de um dispositivo. Armazena o endpoint e as chaves de criptografia
/// que o navegador fornece ao chamar <c>pushManager.subscribe()</c>.
/// Um endpoint é único no sistema: o mesmo dispositivo em contas diferentes não é suportado.
/// </summary>
public sealed class PushInscricao
{
    public Guid Id { get; private set; }
    public Guid UsuarioId { get; private set; }
    public string Endpoint { get; private set; } = default!;

    /// <summary>Chave pública do par de chaves efêmeras do cliente (base64url, sem padding).</summary>
    public string ChaveP256Dh { get; private set; } = default!;

    /// <summary>Segredo de autenticação do cliente (base64url, sem padding).</summary>
    public string ChaveAuth { get; private set; } = default!;

    /// <summary>User-Agent resumido, para diagnóstico (opcional).</summary>
    public string? Agente { get; private set; }

    public DateTimeOffset InscritaEm { get; private set; }
    public DateTimeOffset AtualizadaEm { get; private set; }

    private PushInscricao() { }

    public static PushInscricao Criar(
        Guid usuarioId, string endpoint, string chaveP256Dh, string chaveAuth,
        string? agente, DateTimeOffset agora)
    {
        return new PushInscricao
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            Endpoint = endpoint,
            ChaveP256Dh = chaveP256Dh,
            ChaveAuth = chaveAuth,
            Agente = agente,
            InscritaEm = agora,
            AtualizadaEm = agora,
        };
    }

    /// <summary>Atualiza as chaves quando o navegador rotaciona o par efêmero para o mesmo endpoint.</summary>
    public void AtualizarChaves(string chaveP256Dh, string chaveAuth, string? agente, DateTimeOffset agora)
    {
        ChaveP256Dh = chaveP256Dh;
        ChaveAuth = chaveAuth;
        Agente = agente;
        AtualizadaEm = agora;
    }
}
