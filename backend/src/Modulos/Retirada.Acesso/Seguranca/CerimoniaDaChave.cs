using Fido2NetLib;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Retirada.Acesso.Seguranca;

/// <summary>
/// Guarda, entre os dois passos do WebAuthn, o desafio que o servidor mandou ao aparelho.
/// Vai num cookie HttpOnly cifrado pela Data Protection, vale 5 minutos e é apagado no segundo passo,
/// então cada desafio serve uma vez só. A finalidade amarra o desafio ao uso: o de cadastrar a chave
/// de uma pessoa não serve para entrar, nem para cadastrar a chave de outra.
/// </summary>
public sealed class CerimoniaDaChave(IDataProtectionProvider provedor)
{
    private const string Cookie = "retirada_chave";
    private static readonly TimeSpan Validade = TimeSpan.FromMinutes(5);
    private readonly IDataProtector _cofre = provedor.CreateProtector("Retirada.Acesso.ChaveDeAcesso.v1");

    public void Guardar(HttpContext http, string finalidade, string opcoesJson)
    {
        var protegido = _cofre.CreateProtector(finalidade).ToTimeLimitedDataProtector().Protect(opcoesJson, Validade);
        http.Response.Cookies.Append(Cookie, protegido, new CookieOptions
        {
            HttpOnly = true,
            Secure = http.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/api/acesso",
            MaxAge = Validade,
        });
    }

    /// <summary>Devolve as opções guardadas e apaga o cookie. Null se não houver, se venceu ou se é de outra finalidade.</summary>
    public string? Retirar(HttpContext http, string finalidade)
    {
        if (!http.Request.Cookies.TryGetValue(Cookie, out var protegido) || string.IsNullOrEmpty(protegido)) return null;
        http.Response.Cookies.Delete(Cookie, new CookieOptions { Path = "/api/acesso" });
        try
        {
            return _cofre.CreateProtector(finalidade).ToTimeLimitedDataProtector().Unprotect(protegido);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null; // adulterado, vencido ou de outra finalidade
        }
    }

    /// <summary>
    /// O domínio (RP ID) e as origens aceitas. Fora do desenvolvimento, vêm de Acesso:ChaveDeAcesso
    /// (o compose de produção monta a partir do DOMINIO). A chave fica presa ao domínio: cadastrada em
    /// um endereço, não funciona em outro, e é isso que impede um site falso de usá-la (phishing).
    /// </summary>
    public static Fido2Configuration Configuracao(IConfiguration config)
    {
        var dominio = config["Acesso:ChaveDeAcesso:Dominio"];
        var origens = config["Acesso:ChaveDeAcesso:Origens"];
        return new Fido2Configuration
        {
            RPID = string.IsNullOrWhiteSpace(dominio) ? "localhost" : dominio.Trim(),
            RPName = SegundoFator.Emissor,
            Origins = (string.IsNullOrWhiteSpace(origens) ? "http://localhost:8080,http://localhost:5173" : origens)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(),
            Timeout = 120_000, // 2 min para a pessoa achar o celular e pôr o dedo
        };
    }
}
