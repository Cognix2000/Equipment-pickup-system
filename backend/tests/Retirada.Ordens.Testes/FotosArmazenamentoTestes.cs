using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Retirada.Comum;
using Retirada.Ordens.Infraestrutura.Fotos;
using Retirada.Ordens.Portas;
using Shouldly;

namespace Retirada.Ordens.Testes;

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

public class JpegTestes
{
    [Fact]
    public void Tira_EXIF_e_comentario_e_mantem_a_imagem()
    {
        var original = JpegDeTeste.Montar(largura: 1600, altura: 1200);

        var limpo = Jpeg.Limpar(original);

        limpo.Largura.ShouldBe(1600);
        limpo.Altura.ShouldBe(1200);
        var texto = Encoding.ASCII.GetString(limpo.Conteudo);
        texto.ShouldNotContain("Exif");
        texto.ShouldNotContain("GPS");
        texto.ShouldNotContain("comentario");
        texto.ShouldContain("JFIF");
        limpo.Conteudo.ShouldBe(JpegDeTeste.Montar(1600, 1200, comExif: false)); // igual ao que nunca teve EXIF
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A })] // PNG
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46 })]             // PDF
    [InlineData(new byte[] { 0xFF, 0xD8 })]                         // só o começo
    public void Recusa_o_que_nao_e_JPEG(byte[] dados) =>
        Should.Throw<RegraDeNegocioException>(() => Jpeg.Limpar(dados)).Codigo.ShouldBe("RN14");

    [Fact]
    public void Recusa_JPEG_cortado_no_meio()
    {
        var inteiro = JpegDeTeste.Montar();
        Should.Throw<RegraDeNegocioException>(() => Jpeg.Limpar(inteiro.AsSpan(0, 30))).Codigo.ShouldBe("RN14");
    }
}

/// <summary>O mesmo contrato vale para a pasta e para o S3.</summary>
public abstract class ContratoDoArmazenamento
{
    private protected abstract IArmazenamentoDeFotos Criar();

    protected async Task Guarda_le_copia_e_apaga()
    {
        var armazenamento = Criar();
        var ordem = Guid.NewGuid();
        var foto = Guid.NewGuid();
        var pendente = Chaves.Pendente(ordem, foto);
        var definitiva = Chaves.DaOrdem(ordem, foto);
        var conteudo = JpegDeTeste.Montar(comExif: false);

        (await armazenamento.MetadadosAsync(pendente, default)).ShouldBeNull();
        await armazenamento.GuardarAsync(pendente, conteudo, new Dictionary<string, string> { ["autor"] = "tecnico-1", ["bytes"] = "123" }, default);

        var metadados = (await armazenamento.MetadadosAsync(pendente, default)).ShouldNotBeNull();
        metadados["autor"].ShouldBe("tecnico-1");
        metadados["bytes"].ShouldBe("123");

        await armazenamento.CopiarAsync(pendente, definitiva, default);
        await armazenamento.ApagarAsync(pendente, default);

        (await armazenamento.MetadadosAsync(pendente, default)).ShouldBeNull();
        (await armazenamento.AbrirAsync(pendente, default)).ShouldBeNull();
        (await armazenamento.MetadadosAsync(definitiva, default)).ShouldNotBeNull()["autor"].ShouldBe("tecnico-1");
        await using var lido = (await armazenamento.AbrirAsync(definitiva, default)).ShouldNotBeNull();
        using var memoria = new MemoryStream();
        await lido.CopyToAsync(memoria);
        memoria.ToArray().ShouldBe(conteudo);
    }
}

public class ArmazenamentoEmPastaTestes : ContratoDoArmazenamento
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), $"retirada-fotos-teste-{Guid.NewGuid():N}");

    private protected override IArmazenamentoDeFotos Criar() => new ArmazenamentoEmPasta(_pasta);

    [Fact]
    public Task Contrato() => Guarda_le_copia_e_apaga();

    [Fact]
    public async Task Chave_nao_sai_da_pasta()
    {
        var armazenamento = Criar();
        await Should.ThrowAsync<ArgumentException>(() => armazenamento.GuardarAsync("../fora.jpg", [1], new Dictionary<string, string>(), default));
    }
}

/// <summary>Com RETIRADA_TESTE_S3 (endereço de um MinIO ou S3 de teste); sem a variável, fica ignorado.</summary>
public sealed class FatoComS3Attribute : FactAttribute
{
    public FatoComS3Attribute()
    {
        if (string.IsNullOrWhiteSpace(ArmazenamentoS3Testes.Endereco)) Skip = "Defina RETIRADA_TESTE_S3 para rodar contra o S3.";
    }
}

public class ArmazenamentoS3Testes : ContratoDoArmazenamento
{
    public static string? Endereco => Environment.GetEnvironmentVariable("RETIRADA_TESTE_S3");

    private protected override IArmazenamentoDeFotos Criar() => new ArmazenamentoS3(
        new AmazonS3Client(
            new BasicAWSCredentials(
                Environment.GetEnvironmentVariable("RETIRADA_TESTE_S3_CHAVE") ?? "retirada",
                Environment.GetEnvironmentVariable("RETIRADA_TESTE_S3_SEGREDO") ?? "senha-de-teste"),
            new AmazonS3Config
            {
                ServiceURL = Endereco,
                ForcePathStyle = true,
                AuthenticationRegion = "us-east-1",
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            }),
        $"retirada-teste-{Guid.NewGuid():N}"[..40]);

    [FatoComS3]
    public Task Contrato() => Guarda_le_copia_e_apaga();
}
