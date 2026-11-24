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

/// <summary>
/// Vínculo do técnico: decide como ele recebe o repasse. O CLT recebe na folha; o terceiro resgata por PIX.
/// Os textos batem com o CHECK de usuario.vinculo. Técnico sem vínculo gravado conta como CLT.
/// </summary>
public static class Vinculos
{
    public const string Clt = "clt";
    public const string Terceiro = "terceiro";

    public static bool Valido(string? vinculo) => vinculo is Clt or Terceiro;
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

    /// <summary>Liberar, cancelar e marcar baixa (RN03, RF09, RF19): financeiro ou gestor.</summary>
    public const string Financeiro = "financeiro";

    /// <summary>Retirada e visita sem sucesso (RF12, RF13): só o técnico, e só na ordem dele.</summary>
    public const string Campo = "campo";

    /// <summary>Conferência e devolução na loja (RF14, RF15): estoque ou gestor.</summary>
    public const string Estoque = "estoque";

    /// <summary>Tentativa de contato com o cliente (RN10): financeiro, gestor ou o técnico com a ordem.</summary>
    public const string Contato = "contato";

    /// <summary>Ver quanto cada técnico tem a receber (só leitura, sem registrar pagamento): financeiro, estoque ou gestor.</summary>
    public const string RepasseLeitura = "repasse_leitura";
}

public static class UsuarioAtual
{
    public static Guid Id(this ClaimsPrincipal usuario) =>
        Guid.Parse(usuario.FindFirst(Reivindicacoes.Usuario)?.Value ?? throw new InvalidOperationException("Sessão sem usuário."));

    public static string Papel(this ClaimsPrincipal usuario) => usuario.FindFirst(Reivindicacoes.Papel)?.Value ?? string.Empty;

    /// <summary>RNF04: técnico só enxerga as ordens que estão com ele.</summary>
    public static bool EhTecnico(this ClaimsPrincipal usuario) => usuario.Papel() == Papeis.Tecnico;
}
