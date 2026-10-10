using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Retirada.Banco.Migracoes;

/// <summary>
/// Primeira migração: aplica o modelo de dados revisado (Scripts/0001_modelo_inicial.sql),
/// com gatilhos de só acréscimo, papéis e expurgo, que o EF Core não gera sozinho.
/// </summary>
[DbContext(typeof(MigracaoDbContext))]
[Migration("20261008120000_ModeloInicial")]
public sealed class ModeloInicial : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(Scripts.Ler("0001_modelo_inicial.sql"));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP SCHEMA IF EXISTS retirada CASCADE;");
}
