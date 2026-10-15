using System.Text.Json;
using Retirada.Ordens.Portas;

namespace Retirada.Ordens.Infraestrutura.Fotos;

/// <summary>Fotos numa pasta do disco, com os metadados ao lado (arquivo .json). Para desenvolvimento e testes.</summary>
internal sealed class ArmazenamentoEmPasta(string pasta) : IArmazenamentoDeFotos
{
    public async Task GuardarAsync(string chave, byte[] conteudo, IReadOnlyDictionary<string, string> metadados, CancellationToken ct)
    {
        var caminho = Caminho(chave);
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        await File.WriteAllBytesAsync(caminho, conteudo, ct);
        await File.WriteAllTextAsync(caminho + ".json", JsonSerializer.Serialize(metadados), ct);
    }

    public async Task<IReadOnlyDictionary<string, string>?> MetadadosAsync(string chave, CancellationToken ct)
    {
        var caminho = Caminho(chave) + ".json";
        if (!File.Exists(caminho)) return null;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(caminho, ct));
    }

    public Task CopiarAsync(string de, string para, CancellationToken ct)
    {
        var destino = Caminho(para);
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        File.Copy(Caminho(de), destino, overwrite: true);
        File.Copy(Caminho(de) + ".json", destino + ".json", overwrite: true);
        return Task.CompletedTask;
    }

    public Task<Stream?> AbrirAsync(string chave, CancellationToken ct)
    {
        var caminho = Caminho(chave);
        return Task.FromResult<Stream?>(File.Exists(caminho) ? File.OpenRead(caminho) : null);
    }

    public Task ApagarAsync(string chave, CancellationToken ct)
    {
        File.Delete(Caminho(chave));
        File.Delete(Caminho(chave) + ".json");
        return Task.CompletedTask;
    }

    /// <summary>A chave é montada pelo servidor; mesmo assim, nada sai da pasta.</summary>
    private string Caminho(string chave)
    {
        var raiz = Path.GetFullPath(pasta);
        var caminho = Path.GetFullPath(Path.Combine(raiz, chave));
        if (!caminho.StartsWith(raiz + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Chave de foto inválida.", nameof(chave));
        return caminho;
    }
}
