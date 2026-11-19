using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Retirada.Banco.Migracoes;

/// <summary>Coluna `ordem.plano`: texto livre com o plano do cliente extraído da descrição do HubSoft (nov/2026).</summary>
[DbContext(typeof(MigracaoDbContext))]
[Migration("20261118010000_PlanoOrdem")]
public sealed class PlanoOrdem : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(Scripts.Ler("0009_plano_ordem.sql"));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            SET search_path = retirada, public;
            ALTER TABLE ordem DROP COLUMN IF EXISTS plano;
            """);
}
