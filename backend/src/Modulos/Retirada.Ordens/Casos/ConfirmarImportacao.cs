using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Retirada.Comum;
using Retirada.Ordens.Importacao;
using Retirada.Ordens.Infraestrutura;

namespace Retirada.Ordens.Casos;

/// <summary>
/// UC01, passo 4: o financeiro confirma e tudo é gravado de uma vez. O arquivo vai de novo e precisa ser
/// o mesmo da prévia (mesmo hash): assim nenhum dado pessoal fica guardado entre a prévia e a confirmação.
/// </summary>
internal static class ConfirmarImportacao
{
    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapPost("/{id:guid}/confirmar", ExecutarAsync)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(PreverImportacao.LimiteDaRequisicao))
            .WithSummary("Confirmar e gravar a importação (UC01)")
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

    private static async Task<Results<Ok<RelatorioDaImportacaoDto>, NotFound, ProblemHttpResult>> ExecutarAsync(
        Guid id, IFormFile arquivo, OrdensDbContext db, ClaimsPrincipal usuario, ParametrosDasOrdens parametros,
        TimeProvider relogio, CancellationToken ct)
    {
        var registro = await Gravacao.ObterAsync(db, id, ct);
        if (registro is null || registro.EnviadoPor != usuario.Id()) return TypedResults.NotFound();
        if (registro.Situacao != "previa")
            return Problema(StatusCodes.Status422UnprocessableEntity, "Essa importação já foi confirmada ou descartada.");

        var conteudo = await Processador.LerEnvioAsync(arquivo, ct);
        if (Gravacao.Hash(conteudo) != registro.Hash)
            return Problema(StatusCodes.Status422UnprocessableEntity, "O arquivo não é o mesmo da prévia. Envie de novo para ver a prévia atualizada.");

        // A prévia é refeita agora: se alguém gravou algo no meio tempo, vale o banco de agora.
        var previa = await Processador.PreverAsync(db, conteudo, arquivo.FileName, parametros, ct);
        try
        {
            var autor = new AutorDoEvento(usuario.Id(), usuario.Papel());
            var resultado = await Gravacao.ConfirmarAsync(db, id, previa, autor, relogio.GetUtcNow(), TimeSpan.FromHours(parametros.CarenciaHoras), ct);
            return TypedResults.Ok(new RelatorioDaImportacaoDto(id, ResumoDaImportacaoDto.De(previa),
                resultado.OrdensCriadas, resultado.OrdensAtualizadas, resultado.PendenciasRegistradas, resultado.PendenciasResolvidas));
        }
        catch (DbUpdateException)
        {
            // Outra importação ou ação gravou a mesma ordem (ou série) no meio tempo; a transação desfez tudo.
            return Problema(StatusCodes.Status409Conflict, "Algo mudou enquanto a importação era gravada. Nada foi gravado; envie o arquivo de novo.");
        }
    }

    private static ProblemHttpResult Problema(int status, string titulo) =>
        TypedResults.Problem(title: titulo, statusCode: status, extensions: new Dictionary<string, object?> { ["codigo"] = "IMPORTACAO" });
}

/// <summary>RF05: o relatório do que foi gravado.</summary>
internal sealed record RelatorioDaImportacaoDto(
    Guid ImportacaoId, ResumoDaImportacaoDto Resumo, int OrdensCriadas, int OrdensAtualizadas, int PendenciasRegistradas, int PendenciasResolvidas);
