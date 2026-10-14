using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Retirada.Ordens.Dominio;

namespace Retirada.Ordens.Infraestrutura;

/// <summary>Quem fez a ação e de onde (GPS do aparelho, quando houver).</summary>
internal sealed record AutorDoEvento(Guid UsuarioId, string Papel, double? Latitude = null, double? Longitude = null, double? PrecisaoM = null);

/// <summary>
/// Grava um evento em <c>evento_ordem</c> (RN12: só acréscimo). Roda dentro da transação de quem chama,
/// junto com a mudança da ordem.
/// </summary>
internal static class RegistroDeEventos
{
    public static async Task GravarAsync(
        OrdensDbContext db, Guid ordemId, EventoDaOrdem evento, StatusOrdem? statusDe, StatusOrdem statusPara,
        AutorDoEvento autor, Guid chaveDeIdempotencia, object? dados, CancellationToken ct)
    {
        // O agregado nomeia o evento como "tipo:Motivo" (ex.: visita_sem_sucesso:NaoAchou).
        var partes = evento.Tipo.Split(':', 2);
        var motivo = partes.Length > 1 ? OrdensDbContext.ParaSnake(partes[1]) : null;

        // seq é o próximo número dentro da ordem. A linha da ordem já está travada pelo UPDATE (ou INSERT)
        // desta transação, então outra ação na mesma ordem espera e depois falha na versão.
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO retirada.evento_ordem
                (ordem_id, seq, tipo, status_de, status_para, motivo, ator_id, ator_papel,
                 ocorrido_em, local, precisao_m, chave_idempotencia, dados)
            SELECT @ordem, COALESCE(MAX(seq), 0) + 1, @tipo, @de, @para, @motivo, @ator, @papel,
                   @ocorrido,
                   CASE WHEN @lat IS NULL THEN NULL ELSE ST_SetSRID(ST_MakePoint(@lon, @lat), 4326)::geography END,
                   @precisao, @chave, @dados
            FROM retirada.evento_ordem WHERE ordem_id = @ordem
            """,
            [
                new NpgsqlParameter("ordem", NpgsqlDbType.Uuid) { Value = ordemId },
                new NpgsqlParameter("tipo", NpgsqlDbType.Text) { Value = partes[0] },
                new NpgsqlParameter("de", NpgsqlDbType.Text) { Value = statusDe is null ? DBNull.Value : OrdensDbContext.ParaSnake(statusDe.Value.ToString()) },
                new NpgsqlParameter("para", NpgsqlDbType.Text) { Value = OrdensDbContext.ParaSnake(statusPara.ToString()) },
                new NpgsqlParameter("motivo", NpgsqlDbType.Text) { Value = (object?)motivo ?? DBNull.Value },
                new NpgsqlParameter("ator", NpgsqlDbType.Uuid) { Value = autor.UsuarioId },
                new NpgsqlParameter("papel", NpgsqlDbType.Text) { Value = autor.Papel },
                new NpgsqlParameter("ocorrido", NpgsqlDbType.TimestampTz) { Value = evento.OcorridoEm },
                new NpgsqlParameter("lat", NpgsqlDbType.Double) { Value = (object?)autor.Latitude ?? DBNull.Value },
                new NpgsqlParameter("lon", NpgsqlDbType.Double) { Value = (object?)autor.Longitude ?? DBNull.Value },
                new NpgsqlParameter("precisao", NpgsqlDbType.Real) { Value = (object?)(float?)autor.PrecisaoM ?? DBNull.Value },
                new NpgsqlParameter("chave", NpgsqlDbType.Uuid) { Value = chaveDeIdempotencia },
                new NpgsqlParameter("dados", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(dados ?? new { }) },
            ],
            ct);
    }
}
