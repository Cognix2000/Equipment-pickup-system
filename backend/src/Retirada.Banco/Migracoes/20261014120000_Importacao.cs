using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Retirada.Banco.Migracoes;

/// <summary>RF03–RF05: motivo "sem_rescisao" na pendência e uma pendência aberta por chave.</summary>
[DbContext(typeof(MigracaoDbContext))]
[Migration("20261014120000_Importacao")]
public sealed class Importacao : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(Scripts.Ler("0003_importacao.sql"));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS retirada.pendencia_importacao_aberta_uq;
            ALTER TABLE retirada.pendencia_importacao DROP CONSTRAINT pendencia_importacao_motivo_check;
            ALTER TABLE retirada.pendencia_importacao ADD CONSTRAINT pendencia_importacao_motivo_check
                CHECK (motivo IN ('bloqueio_temporario'));
            """);
}
