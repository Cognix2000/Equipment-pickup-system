using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Retirada.Banco.Migracoes;

/// <summary>ADR-010: colunas de senha, bloqueio e 2FA em usuario, e a tabela de convites.</summary>
[DbContext(typeof(MigracaoDbContext))]
[Migration("20261009120000_Acesso")]
public sealed class Acesso : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(Scripts.Ler("0002_acesso.sql"));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS retirada.convite_usuario;
            ALTER TABLE retirada.usuario
                DROP CONSTRAINT IF EXISTS usuario_email_minusculo,
                DROP COLUMN IF EXISTS senha_hash, DROP COLUMN IF EXISTS falhas_login, DROP COLUMN IF EXISTS bloqueado_ate,
                DROP COLUMN IF EXISTS totp_segredo_cifrado, DROP COLUMN IF EXISTS totp_ativado_em, DROP COLUMN IF EXISTS totp_ultimo_passo,
                DROP COLUMN IF EXISTS carimbo_seguranca, DROP COLUMN IF EXISTS ultimo_login_em;
            """);
}
