namespace Retirada.Ordens.Portas;

/// <summary>
/// Onde ficam os arquivos das fotos (RN14). O banco guarda só os metadados (tabela evidencia);
/// o arquivo fica aqui: numa pasta no desenvolvimento e nos testes, no S3 (MinIO) em homologação e produção.
/// </summary>
internal interface IArmazenamentoDeFotos
{
    Task GuardarAsync(string chave, byte[] conteudo, IReadOnlyDictionary<string, string> metadados, CancellationToken ct);

    /// <returns>Os metadados gravados com o arquivo, ou nulo se o arquivo não existe.</returns>
    Task<IReadOnlyDictionary<string, string>?> MetadadosAsync(string chave, CancellationToken ct);

    Task CopiarAsync(string de, string para, CancellationToken ct);

    /// <returns>O conteúdo, ou nulo se o arquivo não existe.</returns>
    Task<Stream?> AbrirAsync(string chave, CancellationToken ct);

    Task ApagarAsync(string chave, CancellationToken ct);
}
