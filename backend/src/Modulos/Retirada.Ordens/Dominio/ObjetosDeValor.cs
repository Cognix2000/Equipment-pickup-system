using Retirada.Comum;

namespace Retirada.Ordens.Dominio;

/// <summary>Nº de série (SN) da etiqueta, normalizado para comparar sem erro de digitação de espaço ou caixa.</summary>
public sealed record NumeroSerie
{
    public string Valor { get; }

    private NumeroSerie(string valor) => Valor = valor;

    public static NumeroSerie Criar(string? texto)
    {
        var limpo = string.Concat((texto ?? string.Empty).Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        if (limpo.Length is 0 or > 64)
            throw new RegraDeNegocioException("SERIE", "Número de série vazio ou com mais de 64 caracteres.");
        return new NumeroSerie(SnGponLegivel(limpo));
    }

    /// <summary>
    /// A etiqueta da ONU/ONT GPON traz o mesmo SN de duas formas: "HWTC7C8D9E0F" e "485754437C8D9E0F", com as
    /// 4 letras do fabricante em hexadecimal. Guarda sempre a primeira, para o técnico poder digitar qualquer uma.
    /// Os mesmos passos estão em normalizarSerie, no frontend.
    /// </summary>
    private static string SnGponLegivel(string limpo)
    {
        if (limpo.Length != 16 || !limpo.All(char.IsAsciiHexDigit)) return limpo;
        var fabricante = Convert.FromHexString(limpo[..8]);
        return fabricante.All(b => b is >= (byte)'A' and <= (byte)'Z')
            ? System.Text.Encoding.ASCII.GetString(fabricante) + limpo[8..]
            : limpo;
    }

    public override string ToString() => Valor;
}

/// <summary>Ponto no mapa (WGS 84). Ordem sem ponto aparece só na lista (RF06).</summary>
public sealed record Localizacao
{
    public double Latitude { get; }
    public double Longitude { get; }

    public Localizacao(double latitude, double longitude)
    {
        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
            throw new RegraDeNegocioException("LOCALIZACAO", "Coordenada fora do intervalo válido.");
        Latitude = latitude;
        Longitude = longitude;
    }
}

/// <summary>Endereço do serviço (um cliente pode ter dois serviços em endereços diferentes).</summary>
public sealed record Endereco(
    string Logradouro,
    string? Numero,
    string? Complemento,
    string Bairro,
    string Cidade,
    string Uf,
    string? Referencia);
