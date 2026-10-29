using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Retirada.Api.Testes;

/// <summary>
/// Um "celular" de mentira para os testes: faz o papel do aparelho no WebAuthn, com uma chave P-256 (ES256),
/// formato de atestado "none" e a digital sempre confirmada (flags UP e UV). Monta as mesmas respostas
/// que o navegador manda, para a API passar pela verificação de verdade da biblioteca.
/// </summary>
public sealed class AutenticadorDeTeste(string origem = "http://localhost:8080") : IDisposable
{
    private readonly ECDsa _chave = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    public byte[] CredencialId { get; } = RandomNumberGenerator.GetBytes(16);
    public byte[]? ManipuloDoUsuario { get; private set; }
    public uint Contador { get; set; }

    /// <summary>Resposta a <c>navigator.credentials.create</c> para as opções que a API mandou.</summary>
    public object Cadastrar(JsonElement opcoes)
    {
        var rpId = opcoes.GetProperty("rp").GetProperty("id").GetString()!;
        ManipuloDoUsuario = Base64Url.Decodificar(opcoes.GetProperty("user").GetProperty("id").GetString()!);
        var dadosDoCliente = DadosDoCliente("webauthn.create", opcoes.GetProperty("challenge").GetString()!);

        var parametros = _chave.ExportParameters(false);
        var chavePublicaCose = Cbor.Mapa(
            (Cbor.Inteiro(1), Cbor.Inteiro(2)),        // kty: EC2
            (Cbor.Inteiro(3), Cbor.Inteiro(-7)),       // alg: ES256
            (Cbor.Inteiro(-1), Cbor.Inteiro(1)),       // crv: P-256
            (Cbor.Inteiro(-2), Cbor.Bytes(parametros.Q.X!)),
            (Cbor.Inteiro(-3), Cbor.Bytes(parametros.Q.Y!)));

        var dadosDoAutenticador = Concatenar(
            SHA256.HashData(Encoding.UTF8.GetBytes(rpId)),
            [0x45], // UP | UV | AT
            ContadorBigEndian(Contador),
            new byte[16], // AAGUID zerado
            [(byte)(CredencialId.Length >> 8), (byte)CredencialId.Length],
            CredencialId,
            chavePublicaCose);

        var atestado = Cbor.Mapa(
            (Cbor.Texto("fmt"), Cbor.Texto("none")),
            (Cbor.Texto("attStmt"), Cbor.Mapa()),
            (Cbor.Texto("authData"), Cbor.Bytes(dadosDoAutenticador)));

        return new
        {
            id = Base64Url.Codificar(CredencialId),
            rawId = Base64Url.Codificar(CredencialId),
            type = "public-key",
            response = new
            {
                clientDataJSON = Base64Url.Codificar(dadosDoCliente),
                attestationObject = Base64Url.Codificar(atestado),
                transports = new[] { "internal", "hybrid" },
            },
            clientExtensionResults = new { },
        };
    }

    /// <summary>Resposta a <c>navigator.credentials.get</c>: assina o desafio com a chave privada.</summary>
    /// <param name="comDigital">False simula o aparelho que só confirma a presença (UP), sem digital, rosto nem PIN (UV).</param>
    public object Assinar(JsonElement opcoes, ECDsa? outraChave = null, bool comDigital = true)
    {
        Contador++;
        var rpId = opcoes.GetProperty("rpId").GetString()!;
        var dadosDoCliente = DadosDoCliente("webauthn.get", opcoes.GetProperty("challenge").GetString()!);
        var dadosDoAutenticador = Concatenar(SHA256.HashData(Encoding.UTF8.GetBytes(rpId)), [comDigital ? (byte)0x05 : (byte)0x01], ContadorBigEndian(Contador));
        var assinatura = (outraChave ?? _chave).SignData(
            Concatenar(dadosDoAutenticador, SHA256.HashData(dadosDoCliente)), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        return new
        {
            id = Base64Url.Codificar(CredencialId),
            rawId = Base64Url.Codificar(CredencialId),
            type = "public-key",
            response = new
            {
                clientDataJSON = Base64Url.Codificar(dadosDoCliente),
                authenticatorData = Base64Url.Codificar(dadosDoAutenticador),
                signature = Base64Url.Codificar(assinatura),
                userHandle = ManipuloDoUsuario is null ? null : Base64Url.Codificar(ManipuloDoUsuario),
            },
            clientExtensionResults = new { },
        };
    }

    private byte[] DadosDoCliente(string tipo, string desafio) =>
        JsonSerializer.SerializeToUtf8Bytes(new { type = tipo, challenge = desafio, origin = origem, crossOrigin = false });

    private static byte[] ContadorBigEndian(uint valor) => [(byte)(valor >> 24), (byte)(valor >> 16), (byte)(valor >> 8), (byte)valor];

    private static byte[] Concatenar(params byte[][] partes) => partes.SelectMany(p => p).ToArray();

    public void Dispose() => _chave.Dispose();

    private static class Base64Url
    {
        public static string Codificar(byte[] dados) => Convert.ToBase64String(dados).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static byte[] Decodificar(string texto)
        {
            var b64 = texto.Replace('-', '+').Replace('_', '/');
            return Convert.FromBase64String(b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '='));
        }
    }

    /// <summary>O mínimo de CBOR (RFC 8949) que o atestado precisa: inteiros, textos, bytes e mapas.</summary>
    private static class Cbor
    {
        public static byte[] Inteiro(long valor) => valor >= 0 ? Cabecalho(0, (ulong)valor) : Cabecalho(1, (ulong)(-1 - valor));
        public static byte[] Bytes(byte[] dados) => [.. Cabecalho(2, (ulong)dados.Length), .. dados];
        public static byte[] Texto(string texto) => [.. Cabecalho(3, (ulong)Encoding.UTF8.GetByteCount(texto)), .. Encoding.UTF8.GetBytes(texto)];

        public static byte[] Mapa(params (byte[] Chave, byte[] Valor)[] pares) =>
            [.. Cabecalho(5, (ulong)pares.Length), .. pares.SelectMany(p => p.Chave.Concat(p.Valor))];

        private static byte[] Cabecalho(byte tipo, ulong valor) => valor switch
        {
            < 24 => [(byte)((tipo << 5) | (byte)valor)],
            <= byte.MaxValue => [(byte)((tipo << 5) | 24), (byte)valor],
            <= ushort.MaxValue => [(byte)((tipo << 5) | 25), (byte)(valor >> 8), (byte)valor],
            _ => throw new ArgumentOutOfRangeException(nameof(valor), "O teste não precisa de valores tão grandes."),
        };
    }
}
