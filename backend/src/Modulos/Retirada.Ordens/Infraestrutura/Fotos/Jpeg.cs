using Retirada.Comum;

namespace Retirada.Ordens.Infraestrutura.Fotos;

/// <summary>Foto pronta para guardar: sem metadados, com o tamanho em pixels.</summary>
internal sealed record JpegLimpo(byte[] Conteudo, int Largura, int Altura);

/// <summary>
/// RN14 e LGPD: a foto é guardada SEM EXIF nem outros metadados. O GPS e a hora que valem são os da retirada,
/// gravados nas colunas de evidencia. O app já tira os metadados ao comprimir; o servidor tira de novo, porque
/// não confia no cliente. Só JPEG: é o que o app gera, e qualquer outro conteúdo é recusado.
/// </summary>
internal static class Jpeg
{
    /// <summary>Remove APP1 a APP15 (EXIF, XMP, ICC, dados do fabricante) e comentários. O resto passa igual.</summary>
    public static JpegLimpo Limpar(ReadOnlySpan<byte> dados)
    {
        if (dados.Length < 4 || dados[0] != 0xFF || dados[1] != 0xD8)
            throw Recusa("O arquivo não é uma foto JPEG.");

        using var saida = new MemoryStream(dados.Length);
        saida.Write([0xFF, 0xD8]);
        int largura = 0, altura = 0;
        var i = 2;
        while (true)
        {
            if (i + 4 > dados.Length || dados[i] != 0xFF) throw Recusa("A foto está corrompida.");
            var marcador = dados[i + 1];
            if (marcador == 0xFF) { i++; continue; } // byte de preenchimento antes do marcador

            var tamanho = (dados[i + 2] << 8) | dados[i + 3];
            if (tamanho < 2 || i + 2 + tamanho > dados.Length) throw Recusa("A foto está corrompida.");
            var segmento = dados.Slice(i, 2 + tamanho);

            if (EhInicioDeQuadro(marcador) && tamanho >= 7)
            {
                altura = (dados[i + 5] << 8) | dados[i + 6];
                largura = (dados[i + 7] << 8) | dados[i + 8];
            }

            var metadado = marcador is >= 0xE1 and <= 0xEF || marcador == 0xFE;
            if (!metadado) saida.Write(segmento);

            if (marcador == 0xDA)
            {
                // Início da imagem comprimida: daqui até o fim (EOI) vai inteiro.
                saida.Write(dados[(i + 2 + tamanho)..]);
                break;
            }
            i += 2 + tamanho;
        }

        if (largura == 0 || altura == 0) throw Recusa("A foto está corrompida.");
        return new JpegLimpo(saida.ToArray(), largura, altura);
    }

    /// <summary>SOF0 a SOF15, menos DHT (C4), JPG (C8) e DAC (CC), que usam a mesma faixa.</summary>
    private static bool EhInicioDeQuadro(byte marcador) =>
        marcador is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;

    private static RegraDeNegocioException Recusa(string mensagem) => new("RN14", mensagem);
}
