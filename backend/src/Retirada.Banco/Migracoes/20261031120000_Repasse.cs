using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Retirada.Banco.Migracoes;

/// <summary>Repasse ao técnico: crédito por retirada conferida e o pagamento registrado pelo financeiro.</summary>
[DbContext(typeof(MigracaoDbContext))]
[Migration("20261031120000_Repasse")]
public sealed class Repasse : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(Scripts.Ler("0005_repasse.sql"));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP TABLE IF EXISTS retirada.repasse_credito; DROP TABLE IF EXISTS retirada.repasse_pagamento;");
}
