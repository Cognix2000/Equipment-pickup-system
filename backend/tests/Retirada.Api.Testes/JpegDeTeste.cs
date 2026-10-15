using System.Text;

namespace Retirada.Api.Testes;

/// <summary>JPEG fictício montado byte a byte: só a estrutura que o servidor lê, sem imagem de verdade.</summary>
internal static class JpegDeTeste
{
    public static byte[] Montar(int largura = 640, int altura = 480, bool comExif = true)
    {
        var b = new List<byte> { 0xFF, 0xD8 };
        Segmento(b, 0xE0, "JFIF\0"u8.ToArray().Concat(new byte[] { 1, 1, 0, 0, 1, 0, 1, 0, 0 }).ToArray());
        if (comExif)
        {
            Segmento(b, 0xE1, Encoding.ASCII.GetBytes("Exif\0\0GPS -21.0012 -38.9978 MODELO DO CELULAR"));
            Segmento(b, 0xFE, Encoding.ASCII.GetBytes("comentario com dado pessoal"));
        }
        Segmento(b, 0xDB, new byte[65]); // tabela de quantização
        Segmento(b, 0xC0, [8, (byte)(altura >> 8), (byte)altura, (byte)(largura >> 8), (byte)largura, 1, 1, 0x11, 0]);
        Segmento(b, 0xDA, [1, 1, 0, 0, 0x3F, 0]);
        b.AddRange([0x12, 0x34, 0xFF, 0x00, 0x56]); // dados comprimidos (com o FF escapado)
        b.AddRange([0xFF, 0xD9]);
        return [.. b];
    }

    private static void Segmento(List<byte> b, byte marcador, byte[] conteudo)
    {
        var tamanho = conteudo.Length + 2;
        b.AddRange([0xFF, marcador, (byte)(tamanho >> 8), (byte)tamanho]);
        b.AddRange(conteudo);
    }
}
