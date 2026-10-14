using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Retirada.Comum;
using Retirada.Ordens.Importacao;
using Retirada.Ordens.Infraestrutura;

namespace Retirada.Ordens.Casos;

/// <summary>
/// RF03, RF04 e RF05 (UC01): o financeiro envia o arquivo e vê o que vai acontecer com cada linha.
/// Nada é gravado aqui além do registro da prévia (resumo e hash do arquivo, sem dados pessoais).
/// </summary>
internal static class PreverImportacao
{
    /// <summary>10 MB do arquivo (RN15) mais a folga do envelope do formulário.</summary>
    internal const long LimiteDaRequisicao = LeitorDeArquivo.LimiteDeBytes + 64 * 1024;

    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapPost("/previa", ExecutarAsync)
            .DisableAntiforgery() // o cabeçalho X-Retirada já protege contra CSRF (ADR-010)
            .WithMetadata(new RequestSizeLimitAttribute(LimiteDaRequisicao))
            .WithSummary("Prévia da importação, sem gravar (RF03–RF05)")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

    private static async Task<Ok<PreviaDto>> ExecutarAsync(
        IFormFile arquivo, OrdensDbContext db, ClaimsPrincipal usuario, ParametrosDasOrdens parametros, CancellationToken ct)
    {
        var conteudo = await Processador.LerEnvioAsync(arquivo, ct);
        var previa = await Processador.PreverAsync(db, conteudo, arquivo.FileName, parametros, ct);
        var id = await Gravacao.RegistrarPreviaAsync(db, arquivo.FileName, conteudo, usuario.Id(), previa, ct);
        return TypedResults.Ok(PreviaDto.De(id, Path.GetFileName(arquivo.FileName), previa));
    }
}

internal sealed record ResumoDaImportacaoDto(
    int Novas, int Atualizacoes, int Repetidas, int Duplicadas, int Pendencias, int Erros, int OrdensNovas, int OrdensAtualizadas)
{
    public static ResumoDaImportacaoDto De(Previa p) => new(
        p.Contar(ResultadoDaLinha.Nova), p.Contar(ResultadoDaLinha.Atualizacao), p.Contar(ResultadoDaLinha.Repetida),
        p.Contar(ResultadoDaLinha.Duplicada), p.Contar(ResultadoDaLinha.Pendencia), p.Contar(ResultadoDaLinha.Erro),
        p.Novas.Count, p.Atualizacoes.Count);
}

internal sealed record LinhaDaPreviaDto(int Numero, ResultadoDaLinha Resultado, string? Cliente, string? Endereco, string? Bairro, string? TipoOs, IReadOnlyList<string> Detalhes);

internal sealed record PreviaDto(
    Guid ImportacaoId,
    string Arquivo,
    FormatoDoArquivo Formato,
    int TotalDeLinhas,
    ResumoDaImportacaoDto Resumo,
    IReadOnlyList<string> ColunasReconhecidas,
    IReadOnlyList<string> ColunasDescartadas,
    IReadOnlyList<string> Avisos,
    IReadOnlyList<LinhaDaPreviaDto> Linhas)
{
    public static PreviaDto De(Guid id, string arquivo, Previa p) => new(
        id, arquivo, p.Formato, p.Linhas.Count, ResumoDaImportacaoDto.De(p),
        p.Colunas.Reconhecidas, p.Colunas.Descartadas, p.AvisosDoArquivo,
        [.. p.Linhas.Select(l => new LinhaDaPreviaDto(l.Numero, l.Resultado, l.Cliente, l.Endereco, l.Bairro, l.TipoOs, l.Detalhes))]);
}
