using System.Text.RegularExpressions;
using Retirada.Comum;

namespace Retirada.Ordens.Dominio;

/// <summary>Tipos de chave PIX; os textos batem com o CHECK de chave_pix.tipo.</summary>
public static class TiposDeChavePix
{
    public const string Cpf = "cpf";
    public const string Cnpj = "cnpj";
    public const string Email = "email";
    public const string Telefone = "telefone";
    public const string Aleatoria = "aleatoria";
}

/// <summary>Uma chave PIX já conferida e no formato que o gateway espera.</summary>
public sealed partial record ChavePix(string Tipo, string Chave)
{
    /// <summary>
    /// Confere o formato e normaliza: CPF e CNPJ só com números e dígitos verificadores certos, e-mail em minúsculas,
    /// telefone em E.164 (+55 com DDD) e chave aleatória no formato UUID. Não prova que a chave é do técnico:
    /// para isso o financeiro aprova a chave antes do primeiro resgate. <paramref name="modoSimulacao"/> aceita o
    /// DDD 00 dos dados fictícios, como no RN05.
    /// </summary>
    public static ChavePix Validar(string? tipo, string? chave, bool modoSimulacao = false)
    {
        var texto = (chave ?? string.Empty).Trim();
        return tipo switch
        {
            TiposDeChavePix.Cpf when Digitos(texto) is { Length: 11 } cpf && DocumentoValido(cpf) => new(tipo, cpf),
            TiposDeChavePix.Cnpj when Digitos(texto) is { Length: 14 } cnpj && DocumentoValido(cnpj) => new(tipo, cnpj),
            TiposDeChavePix.Email when texto.Length <= 77 && Email().IsMatch(texto) => new(tipo, texto.ToLowerInvariant()),
            TiposDeChavePix.Telefone when Telefone(texto, modoSimulacao) is { } e164 => new(tipo, e164),
            TiposDeChavePix.Aleatoria when Guid.TryParseExact(texto, "D", out var evp) => new(tipo, evp.ToString()),
            TiposDeChavePix.Cpf or TiposDeChavePix.Cnpj or TiposDeChavePix.Email or TiposDeChavePix.Telefone or TiposDeChavePix.Aleatoria =>
                throw new RegraDeNegocioException("PIX_CHAVE_INVALIDA", $"Confira a chave PIX: não parece um {Rotulo(tipo)} válido."),
            _ => throw new RegraDeNegocioException("PIX_CHAVE_INVALIDA", "Escolha o tipo da chave PIX: CPF, CNPJ, e-mail, telefone ou aleatória."),
        };
    }

    /// <summary>Mostra só o começo e o fim da chave, para a tela e o histórico não exporem o CPF inteiro.</summary>
    public string Mascarada() => Tipo switch
    {
        TiposDeChavePix.Cpf => $"***.{Chave[3..6]}.{Chave[6..9]}-**",
        TiposDeChavePix.Cnpj => $"**.{Chave[2..5]}.{Chave[5..8]}/****-**",
        TiposDeChavePix.Telefone => $"{Chave[..5]} *****-{Chave[^4..]}",
        TiposDeChavePix.Email => $"{Chave[..Math.Min(2, Chave.IndexOf('@'))]}***{Chave[Chave.IndexOf('@')..]}",
        _ => $"{Chave[..4]}****-****-{Chave[^4..]}",
    };

    private static string Rotulo(string tipo) => tipo switch
    {
        TiposDeChavePix.Cpf => "CPF",
        TiposDeChavePix.Cnpj => "CNPJ",
        TiposDeChavePix.Email => "e-mail",
        TiposDeChavePix.Telefone => "celular com DDD",
        _ => "código de chave aleatória",
    };

    private static string Digitos(string texto) => new(texto.Where(char.IsAsciiDigit).ToArray());

    /// <summary>Dígitos verificadores do CPF (11) e do CNPJ (14), pelo módulo 11. Repetidos (111...) não valem.</summary>
    internal static bool DocumentoValido(string digitos)
    {
        if (digitos.Distinct().Count() == 1) return false;
        var corpo = digitos.Length - 2;
        for (var posicao = corpo; posicao < digitos.Length; posicao++)
        {
            var soma = 0;
            for (var i = 0; i < posicao; i++)
            {
                var peso = digitos.Length == 11 ? posicao + 1 - i : (posicao - i - 1) % 8 + 2;
                soma += (digitos[i] - '0') * peso;
            }
            var resto = soma % 11;
            if (digitos[posicao] - '0' != (resto < 2 ? 0 : 11 - resto)) return false;
        }
        return true;
    }

    /// <summary>Celular brasileiro: DDD e 9 dígitos começando por 9. Aceita com ou sem +55.</summary>
    private static string? Telefone(string texto, bool modoSimulacao)
    {
        var digitos = Digitos(texto);
        if (digitos.Length == 13 && digitos.StartsWith("55", StringComparison.Ordinal)) digitos = digitos[2..];
        var dddValido = digitos.Length == 11 && (digitos[0] != '0' || (modoSimulacao && digitos.StartsWith("00", StringComparison.Ordinal)));
        return dddValido && digitos[2] == '9' ? $"+55{digitos}" : null;
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex Email();
}

/// <summary>Situação do resgate; os textos batem com o CHECK de resgate.situacao.</summary>
public static class SituacoesDoResgate
{
    public const string Solicitado = "solicitado"; // gravado; o pedido ao gateway ainda não voltou
    public const string Enviado = "enviado";       // o gateway aceitou; falta a confirmação do PIX
    public const string Pago = "pago";             // o PIX caiu: tem ID de Fim a Fim
    public const string Falhou = "falhou";         // o crédito voltou ao saldo
}

/// <summary>
/// Resgate do técnico terceiro, como o saque do entregador de aplicativo: ele aperta "Resgatar" e o saldo das
/// retiradas que o estoque já conferiu vai por PIX para a chave aprovada. O CLT recebe na folha e não resgata.
/// </summary>
public static class RegrasDoResgate
{
    public static void ConferirSePodeResgatar(string? vinculo, bool temChaveAprovada, bool temResgateEmAndamento, int disponivelCentavos, int minimoCentavos)
    {
        if ((vinculo ?? Vinculos.Clt) != Vinculos.Terceiro)
            throw new RegraDeNegocioException("RESGATE_SO_TERCEIRO", "O técnico CLT recebe o repasse na folha, no fim do mês. O resgate por PIX é só para terceiros.");
        if (!temChaveAprovada)
            throw new RegraDeNegocioException("RESGATE_SEM_CHAVE", "Cadastre a chave PIX e espere o financeiro aprovar antes do primeiro resgate.");
        if (temResgateEmAndamento)
            throw new RegraDeNegocioException("RESGATE_EM_ANDAMENTO", "Já tem um resgate a caminho. Espere ele cair para pedir outro.");
        if (disponivelCentavos < minimoCentavos)
            throw new RegraDeNegocioException("RESGATE_ABAIXO_DO_MINIMO",
                $"O resgate mínimo é de {Reais(minimoCentavos)}. Hoje você tem {Reais(disponivelCentavos)} das retiradas já conferidas pelo estoque.");
    }

    public static string Reais(int centavos) => (centavos / 100m).ToString("C", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
}
