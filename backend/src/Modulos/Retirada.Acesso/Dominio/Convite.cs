using System.Security.Cryptography;
using System.Text;
using Retirada.Comum;

namespace Retirada.Acesso.Dominio;

/// <summary>
/// Link de primeiro acesso (ou de nova senha). O token em claro só existe no link;
/// o banco guarda o SHA-256 dele, então quem lê o banco não consegue usar o convite.
/// </summary>
public sealed class Convite : Entidade
{
    public static readonly TimeSpan Validade = TimeSpan.FromHours(48);

    public Guid UsuarioId { get; private set; }
    // Stryker disable once String : valor inicial só para o EF Core
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiraEm { get; private set; }
    public DateTimeOffset? UsadoEm { get; private set; }
    public Guid CriadoPor { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }

    private Convite() { } // EF Core

    public static (Convite Convite, string Token) Gerar(Guid usuarioId, Guid criadoPor, DateTimeOffset agora)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var convite = new Convite
        {
            UsuarioId = usuarioId,
            TokenHash = Hash(token),
            ExpiraEm = agora + Validade,
            CriadoPor = criadoPor,
            CriadoEm = agora,
        };
        return (convite, token);
    }

    public static string Hash(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public void Usar(DateTimeOffset agora)
    {
        if (UsadoEm is not null)
            throw new RegraDeNegocioException("CONVITE_USADO", "Este link já foi usado. Peça um novo ao gestor.");
        if (agora >= ExpiraEm)
            throw new RegraDeNegocioException("CONVITE_EXPIRADO", "Este link venceu. Peça um novo ao gestor.");
        UsadoEm = agora;
    }
}
