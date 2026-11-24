using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Retirada.Banco.Migracoes;

/// <summary>
/// Cria a tabela <c>push_inscricao</c> para armazenar as inscrições de Web Push (VAPID).
/// Cada linha representa um dispositivo (endpoint de push do navegador) associado a um usuário.
/// O técnico se inscreve no app de campo e passa a receber notificações de ordem designada,
/// carência vencendo e lembrete de visita.
/// </summary>
[DbContext(typeof(MigracaoDbContext))]
[Migration("20261124000000_PushInscricao")]
public sealed class PushInscricao : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(Scripts.Ler("0010_push_inscricao.sql"));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            SET search_path = retirada, public;
            DROP TABLE IF EXISTS push_inscricao;
            """);
}
