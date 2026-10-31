using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Retirada.Banco.Migracoes;

/// <summary>Última posição de cada técnico, para o mapa da equipe.</summary>
[DbContext(typeof(MigracaoDbContext))]
[Migration("20261031150000_PosicaoTecnico")]
public sealed class PosicaoTecnico : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(Scripts.Ler("0006_posicao_tecnico.sql"));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP TABLE IF EXISTS retirada.posicao_tecnico;");
}
