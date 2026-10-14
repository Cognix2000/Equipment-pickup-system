using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Retirada.Banco;

/// <summary>Aplica as migrações pendentes e, só em homologação, a carga de dados fictícios.</summary>
public static class Migrador
{
    private const long TravaDeMigracao = 20261008; // número fixo e exclusivo das migrações deste sistema

    public static async Task AplicarAsync(string connectionString, string? arquivoDadosFicticios, TextWriter log, CancellationToken ct = default)
    {
        await using (var db = MigracaoDbContext.Criar(connectionString))
        {
            // Dois migradores ao mesmo tempo (duas réplicas subindo, ou suítes de teste em paralelo)
            // aplicariam a mesma migração duas vezes. A trava do banco põe um na fila do outro.
            await db.Database.OpenConnectionAsync(ct);
            await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_lock({TravaDeMigracao})", ct);
            try
            {
                var pendentes = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
                log.WriteLine(pendentes.Count == 0 ? "Banco em dia." : $"Aplicando: {string.Join(", ", pendentes)}");
                await db.Database.MigrateAsync(ct);
            }
            finally
            {
                await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_unlock({TravaDeMigracao})", CancellationToken.None);
            }
        }

        if (arquivoDadosFicticios is null) return;

        // A carga é idempotente (ON CONFLICT DO NOTHING): rodar de novo não duplica.
        var sql = await File.ReadAllTextAsync(arquivoDadosFicticios, ct);
        await using var conexao = new NpgsqlConnection(connectionString);
        await conexao.OpenAsync(ct);
        await using var comando = new NpgsqlCommand(sql, conexao);
        await comando.ExecuteNonQueryAsync(ct);
        log.WriteLine($"Dados fictícios carregados de {Path.GetFileName(arquivoDadosFicticios)}.");
    }
}
