using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Retirada.Banco.Migracoes;

/// <summary>ADR-012: chave de acesso (passkey) como alternativa à senha e ao aplicativo autenticador.</summary>
[DbContext(typeof(MigracaoDbContext))]
[Migration("20261028120000_ChaveDeAcesso")]
public sealed class ChaveDeAcesso : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(Scripts.Ler("0004_chave_de_acesso.sql"));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP TABLE IF EXISTS retirada.chave_de_acesso;");
}
