using System.Security.Claims;

namespace Retirada.Comum;

/// <summary>Nomes das informações que a sessão carrega (ADR-010). Compartilhados por todos os módulos.</summary>
public static class Reivindicacoes
{
    public const string Usuario = "sub";
    public const string Nome = "nome";
    public const string Papel = "papel";
    public const string Carimbo = "carimbo";
    public const string Etapa = "etapa";
}

/// <summary>Papéis do sistema; os textos batem com o CHECK de usuario.papel.</summary>
public static class Papeis
{
    public const string Gestor = "admin";
    public const string Financeiro = "financeiro";
    public const string Tecnico = "tecnico";
    public const string Parceiro = "parceiro";
    public const string Estoque = "estoque";
}

/// <summary>Em que ponto do login a sessão está. Só "completo" acessa o sistema.</summary>
public static class EtapasDeAcesso
{
    public const string Completo = "completo";
    public const string Codigo = "codigo_2fa";
    public const string Configurar = "configurar_2fa";
}

/// <summary>Políticas de autorização usadas pelos endpoints dos módulos.</summary>
public static class Politicas
{
    /// <summary>Logado, mas ainda no meio do 2FA: só serve para terminar o login.</summary>
    public const string LoginParcial = "login_parcial";
    public const string Gestao = "gestao";
    public const string LerOrdens = "ler_ordens";
}

public static class UsuarioAtual
{
    public static Guid Id(this ClaimsPrincipal usuario) =>
        Guid.Parse(usuario.FindFirst(Reivindicacoes.Usuario)?.Value ?? throw new InvalidOperationException("Sessão sem usuário."));

    public static string Papel(this ClaimsPrincipal usuario) => usuario.FindFirst(Reivindicacoes.Papel)?.Value ?? string.Empty;

    /// <summary>RNF04: técnico só enxerga as ordens que estão com ele.</summary>
    public static bool EhTecnico(this ClaimsPrincipal usuario) => usuario.Papel() == Papeis.Tecnico;
}
