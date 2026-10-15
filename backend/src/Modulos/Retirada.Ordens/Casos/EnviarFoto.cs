using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Retirada.Comum;
using Retirada.Ordens.Dominio;
using Retirada.Ordens.Infraestrutura;
using Retirada.Ordens.Infraestrutura.Fotos;
using Retirada.Ordens.Portas;

namespace Retirada.Ordens.Casos;

/// <summary>
/// RN14: o técnico envia cada foto assim que tira, uma por requisição (sinal fraco não perde as outras).
/// A foto fica pendente até a retirada ser registrada; se nunca for usada, o armazenamento apaga sozinho.
/// </summary>
internal static class EnviarFoto
{
    /// <summary>O app comprime para algumas centenas de KB; 8 MB é folga para a foto que vier sem compressão.</summary>
    internal const long LimiteDeBytes = 8 * 1024 * 1024;

    internal sealed record FotoEnviadaDto(Guid Id, int Largura, int Altura, int Bytes);

    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapPost("/{id:guid}/fotos", ExecutarAsync)
            .RequireAuthorization(Politicas.Campo)
            .DisableAntiforgery() // o cabeçalho X-Retirada já protege contra CSRF (ADR-010)
            .WithMetadata(new RequestSizeLimitAttribute(LimiteDeBytes + 64 * 1024))
            .WithSummary("Enviar uma foto da retirada, antes de registrá-la (RN14)")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

    private static async Task<Results<Ok<FotoEnviadaDto>, NotFound, ProblemHttpResult>> ExecutarAsync(
        Guid id, IFormFile foto, OrdensDbContext db, IArmazenamentoDeFotos armazenamento, ClaimsPrincipal usuario, CancellationToken ct)
    {
        // RNF04: só o técnico com a ordem envia foto dela; para os outros, a ordem não existe.
        var tecnico = usuario.Id();
        var ordem = await db.Ordens.AsNoTracking()
            .Where(o => o.Id == id && o.ResponsavelId == tecnico)
            .Select(o => new { o.Status })
            .SingleOrDefaultAsync(ct);
        if (ordem is null) return TypedResults.NotFound();
        if (ordem.Status is not (StatusOrdem.Atribuida or StatusOrdem.EmRota))
            throw new RegraDeNegocioException("RN14", "Essa ordem não está mais em retirada.");
        if (foto.Length is 0 or > LimiteDeBytes)
            throw new RegraDeNegocioException("RN14", "A foto precisa ter até 8 MB.");

        byte[] dados;
        await using (var leitura = foto.OpenReadStream())
        using (var memoria = new MemoryStream((int)foto.Length))
        {
            await leitura.CopyToAsync(memoria, ct);
            dados = memoria.ToArray();
        }

        var limpo = Jpeg.Limpar(dados);
        var fotoId = Guid.CreateVersion7();
        await armazenamento.GuardarAsync(Chaves.Pendente(id, fotoId), limpo.Conteudo, new Dictionary<string, string>
        {
            [MetadadosDaFoto.Autor] = tecnico.ToString(),
            [MetadadosDaFoto.Sha256] = Convert.ToHexStringLower(SHA256.HashData(limpo.Conteudo)),
            [MetadadosDaFoto.Bytes] = limpo.Conteudo.Length.ToString(CultureInfo.InvariantCulture),
            [MetadadosDaFoto.Largura] = limpo.Largura.ToString(CultureInfo.InvariantCulture),
            [MetadadosDaFoto.Altura] = limpo.Altura.ToString(CultureInfo.InvariantCulture),
        }, ct);

        return TypedResults.Ok(new FotoEnviadaDto(fotoId, limpo.Largura, limpo.Altura, limpo.Conteudo.Length));
    }
}

/// <summary>Os metadados que a API grava junto com a foto pendente. Quem envia não consegue mexer neles.</summary>
internal static class MetadadosDaFoto
{
    public const string Autor = "autor";
    public const string Sha256 = "sha256";
    public const string Bytes = "bytes";
    public const string Largura = "largura";
    public const string Altura = "altura";
}
