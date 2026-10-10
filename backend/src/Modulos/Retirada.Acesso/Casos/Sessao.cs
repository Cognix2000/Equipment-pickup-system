using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Retirada.Acesso.Dominio;
using Retirada.Comum;

namespace Retirada.Acesso.Casos;

internal sealed record UsuarioDto(Guid Id, string Nome, string Email, string Papel)
{
    public static UsuarioDto De(Usuario u) => new(u.Id, u.Nome, u.Email, u.Papel);
}

/// <summary>"pronto" = entrou; "codigo" = falta o código do aplicativo; "configurar" = falta cadastrar o aplicativo.</summary>
internal sealed record RespostaDeLogin(string Etapa, UsuarioDto? Usuario);

/// <summary>Monta a sessão no cookie HttpOnly (ADR-010) e padroniza as respostas de recusa.</summary>
internal static class Sessao
{
    public const string Esquema = CookieAuthenticationDefaults.AuthenticationScheme;

    public static Task AbrirAsync(HttpContext http, Usuario usuario, string etapa)
    {
        var identidade = new ClaimsIdentity(
            [
                new Claim(Reivindicacoes.Usuario, usuario.Id.ToString()),
                new Claim(Reivindicacoes.Nome, usuario.Nome),
                new Claim(Reivindicacoes.Papel, usuario.Papel),
                new Claim(Reivindicacoes.Carimbo, usuario.CarimboSeguranca.ToString()),
                new Claim(Reivindicacoes.Etapa, etapa),
            ],
            Esquema, Reivindicacoes.Nome, Reivindicacoes.Papel);

        return http.SignInAsync(Esquema, new ClaimsPrincipal(identidade), new AuthenticationProperties { IsPersistent = true });
    }

    public static Task FecharAsync(HttpContext http) => http.SignOutAsync(Esquema);

    public static string Etapa(ClaimsPrincipal usuario) => usuario.FindFirstValue(Reivindicacoes.Etapa) ?? string.Empty;

    public static IPAddress? Ip(HttpContext http) => http.Connection.RemoteIpAddress;

    public static ProblemHttpResult Recusa(string mensagem, string codigo, int status = StatusCodes.Status401Unauthorized) =>
        TypedResults.Problem(title: mensagem, statusCode: status, extensions: new Dictionary<string, object?> { ["codigo"] = codigo });

    public static ProblemHttpResult EtapaErrada() =>
        Recusa("Este passo do login não vale agora. Entre de novo.", "ETAPA_ERRADA", StatusCodes.Status409Conflict);
}
