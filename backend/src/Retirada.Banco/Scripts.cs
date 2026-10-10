using System.Reflection;

namespace Retirada.Banco;

internal static class Scripts
{
    public static string Ler(string nome)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var recurso = assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + nome, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(recurso)!;
        using var leitor = new StreamReader(stream);
        return leitor.ReadToEnd();
    }
}
