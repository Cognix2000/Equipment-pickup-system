using Microsoft.AspNetCore.DataProtection;
using OtpNet;

namespace Retirada.Acesso.Seguranca;

/// <summary>
/// TOTP de 6 dígitos a cada 30 s (RFC 6238), o que Google Authenticator, Microsoft Authenticator
/// e afins leem. O segredo vai cifrado para o banco com a Data Protection do ASP.NET.
/// </summary>
public sealed class SegundoFator(IDataProtectionProvider provedor)
{
    public const string Emissor = "Retirada Netwise";
    private readonly IDataProtector _cofre = provedor.CreateProtector("Retirada.Acesso.Totp.v1");

    public (string SegredoBase32, string SegredoCifrado) NovoSegredo()
    {
        var chave = KeyGeneration.GenerateRandomKey(20);
        var base32 = Base32Encoding.ToString(chave);
        return (base32, _cofre.Protect(base32));
    }

    public static string EnderecoOtpAuth(string email, string segredoBase32) =>
        $"otpauth://totp/{Uri.EscapeDataString(Emissor)}:{Uri.EscapeDataString(email)}" +
        $"?secret={segredoBase32}&issuer={Uri.EscapeDataString(Emissor)}&digits=6&period=30";

    /// <summary>Aceita o código do passo atual ou de um vizinho (relógio do celular adiantado ou atrasado 30 s).</summary>
    public long? Verificar(string segredoCifrado, string? codigo, DateTimeOffset agora)
    {
        if (string.IsNullOrWhiteSpace(codigo) || codigo.Trim().Length != 6) return null;
        var totp = new Totp(Base32Encoding.ToBytes(_cofre.Unprotect(segredoCifrado)));
        return totp.VerifyTotp(agora.UtcDateTime, codigo.Trim(), out var passo, new VerificationWindow(previous: 1, future: 1))
            ? passo
            : null;
    }
}
