using Microsoft.AspNetCore.Http;
using Retirada.Comum;
using Retirada.Ordens.Casos;
using Retirada.Ordens.Infraestrutura;

namespace Retirada.Ordens.Importacao;

/// <summary>O caminho da prévia, igual na primeira vez e na confirmação: ler, reconhecer, validar e comparar com o banco.</summary>
internal static class Processador
{
    public static async Task<Previa> PreverAsync(OrdensDbContext db, byte[] conteudo, string nomeDoArquivo, ParametrosDasOrdens parametros, CancellationToken ct)
    {
        var arquivo = LeitorDeArquivo.Ler(conteudo, nomeDoArquivo);
        var colunas = Colunas.Mapear(arquivo.Cabecalhos);
        var linhas = arquivo.Linhas.Select(l => Interpretador.Interpretar(l, colunas, parametros.ModoSimulacao)).ToList();
        var retrato = await Gravacao.CarregarRetratoAsync(db, [.. linhas.Select(l => l.Ordem).OfType<OrdemLida>()], ct);
        return MontadorDaPrevia.Montar(arquivo, colunas, linhas, retrato);
    }

    /// <summary>RN15: acima de 10 MB, recusa sem ler o conteúdo.</summary>
    public static async Task<byte[]> LerEnvioAsync(IFormFile arquivo, CancellationToken ct)
    {
        if (arquivo.Length > LeitorDeArquivo.LimiteDeBytes)
            throw new RegraDeNegocioException("RN15", "O arquivo passa de 10 MB. Divida a planilha em partes menores.");
        using var memoria = new MemoryStream((int)arquivo.Length);
        await arquivo.CopyToAsync(memoria, ct);
        return memoria.ToArray();
    }
}
