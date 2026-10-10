using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Retirada.Comum;

namespace Retirada.Acesso.Seguranca;

/// <summary>
/// RNF03: Argon2id com os parâmetros mínimos da OWASP (19 MiB, 2 iterações, 1 linha).
/// Guarda no formato PHC ($argon2id$v=19$m=...,t=...,p=...$sal$hash), que carrega os
/// parâmetros junto: subir o custo depois não invalida as senhas antigas.
/// </summary>
public static class HashDeSenha
{
    private const int MemoriaKiB = 19_456;
    private const int Iteracoes = 2;
    private const int Paralelismo = 1;
    private const int TamanhoSal = 16;
    private const int TamanhoHash = 32;

    public const int TamanhoMinimo = 12;
    public const int TamanhoMaximo = 128;

    /// <summary>Hash de referência para gastar o mesmo tempo quando o e-mail não existe.</summary>
    private static readonly Lazy<string> HashFalso = new(() => Gerar("senha-que-nao-existe-de-verdade"));

    public static void ValidarPolitica(string senha, string email)
    {
        if (senha.Length < TamanhoMinimo)
            throw new RegraDeNegocioException("SENHA_CURTA", $"A senha precisa de pelo menos {TamanhoMinimo} caracteres.");
        if (senha.Length > TamanhoMaximo)
            throw new RegraDeNegocioException("SENHA_LONGA", $"A senha pode ter no máximo {TamanhoMaximo} caracteres.");
        if (senha.Contains(email.Split('@')[0], StringComparison.OrdinalIgnoreCase))
            throw new RegraDeNegocioException("SENHA_COM_EMAIL", "A senha não pode conter o seu e-mail.");
    }

    public static string Gerar(string senha)
    {
        var sal = RandomNumberGenerator.GetBytes(TamanhoSal);
        var hash = Calcular(senha, sal, MemoriaKiB, Iteracoes, Paralelismo, TamanhoHash);
        return $"$argon2id$v=19$m={MemoriaKiB},t={Iteracoes},p={Paralelismo}${Convert.ToBase64String(sal)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verificar(string senha, string? guardado)
    {
        if (guardado is null)
        {
            Verificar(senha, HashFalso.Value); // mesmo tempo de resposta, com ou sem usuário
            return false;
        }

        var partes = guardado.Split('$');
        if (partes.Length != 6 || partes[1] != "argon2id") return false;
        var parametros = partes[3].Split(',').Select(p => p.Split('=')).ToDictionary(p => p[0], p => int.Parse(p[1]));
        var sal = Convert.FromBase64String(partes[4]);
        var esperado = Convert.FromBase64String(partes[5]);
        var calculado = Calcular(senha, sal, parametros["m"], parametros["t"], parametros["p"], esperado.Length);
        return CryptographicOperations.FixedTimeEquals(calculado, esperado);
    }

    private static byte[] Calcular(string senha, byte[] sal, int memoria, int iteracoes, int paralelismo, int tamanho)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(senha))
        {
            Salt = sal,
            MemorySize = memoria,
            Iterations = iteracoes,
            DegreeOfParallelism = paralelismo,
        };
        return argon.GetBytes(tamanho);
    }
}
