using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Retirada.Banco.Migracoes;

/// <summary>
/// RF02 (nov/2026): acrescenta a situação 'apagado' ao CHECK de `usuario.situacao`. O gestor pode
/// apagar um usuário já revogado (ou pendente) — a linha fica para o histórico (RN12) manter o autor,
/// mas sem dado pessoal e sem acesso. Ver ADR-010.
/// </summary>
[DbContext(typeof(MigracaoDbContext))]
[Migration("20261118000000_UsuarioApagado")]
public sealed class UsuarioApagado : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(Scripts.Ler("0008_usuario_apagado.sql"));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            SET search_path = retirada, public;
            ALTER TABLE usuario DROP CONSTRAINT IF EXISTS usuario_situacao_check;
            ALTER TABLE usuario ADD CONSTRAINT usuario_situacao_check
                CHECK (situacao IN ('pendente', 'ativo', 'suspenso', 'revogado'));
            """);
}
