using Microsoft.EntityFrameworkCore;

namespace Retirada.Banco;

/// <summary>
/// Contexto só para aplicar migrações. Roda com o usuário dono do banco; a API roda com
/// retirada_api, que herda retirada_app e não tem DELETE (RN12).
/// </summary>
public sealed class MigracaoDbContext(DbContextOptions<MigracaoDbContext> options) : DbContext(options)
{
    public static MigracaoDbContext Criar(string connectionString) =>
        new(new DbContextOptionsBuilder<MigracaoDbContext>()
            .UseNpgsql(connectionString, o => o.MigrationsHistoryTable("__EFMigrationsHistory", "public"))
            .Options);
}
