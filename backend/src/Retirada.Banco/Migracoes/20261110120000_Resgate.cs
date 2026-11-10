using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Retirada.Banco.Migracoes;

/// <summary>Resgate do técnico terceiro por PIX: vínculo do técnico, chave PIX aprovada e resgates.</summary>
[DbContext(typeof(MigracaoDbContext))]
[Migration("20261110120000_Resgate")]
public sealed class Resgate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(Scripts.Ler("0007_resgate.sql"));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            ALTER TABLE retirada.repasse_credito DROP COLUMN IF EXISTS resgate_id;
            DROP TABLE IF EXISTS retirada.resgate;
            DROP TABLE IF EXISTS retirada.chave_pix;
            ALTER TABLE retirada.usuario DROP CONSTRAINT IF EXISTS usuario_vinculo_so_de_tecnico;
            ALTER TABLE retirada.usuario DROP COLUMN IF EXISTS vinculo;
            """);
}
