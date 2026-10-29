using System.Net;
using Microsoft.EntityFrameworkCore;
using Retirada.Acesso.Dominio;

namespace Retirada.Acesso.Infraestrutura;

/// <summary>Contexto do módulo Acesso: usuários, convites, chaves de acesso e a trilha de auditoria (só acréscimo).</summary>
public sealed class AcessoDbContext(DbContextOptions<AcessoDbContext> options) : DbContext(options)
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Convite> Convites => Set<Convite>();
    public DbSet<ChaveDeAcesso> ChavesDeAcesso => Set<ChaveDeAcesso>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("retirada");

        modelBuilder.Entity<Usuario>(u =>
        {
            u.ToTable("usuario");
            u.HasKey(x => x.Id);
            u.Property(x => x.Id).HasColumnName("id");
            u.Property(x => x.Nome).HasColumnName("nome");
            u.Property(x => x.Email).HasColumnName("email");
            u.Property(x => x.Papel).HasColumnName("papel");
            u.Property(x => x.Situacao).HasColumnName("situacao").HasConversion(
                s => s.ToString().ToLowerInvariant(),
                s => Enum.Parse<SituacaoUsuario>(s, true));
            u.Property(x => x.SenhaHash).HasColumnName("senha_hash");
            u.Property(x => x.FalhasLogin).HasColumnName("falhas_login");
            u.Property(x => x.BloqueadoAte).HasColumnName("bloqueado_ate");
            u.Property(x => x.TotpSegredoCifrado).HasColumnName("totp_segredo_cifrado");
            u.Property(x => x.TotpAtivadoEm).HasColumnName("totp_ativado_em");
            u.Property(x => x.TotpUltimoPasso).HasColumnName("totp_ultimo_passo");
            u.Property(x => x.CarimboSeguranca).HasColumnName("carimbo_seguranca").IsConcurrencyToken();
            u.Property(x => x.UltimoLoginEm).HasColumnName("ultimo_login_em");
            u.Property(x => x.AprovadoPor).HasColumnName("aprovado_por");
            u.Property(x => x.AprovadoEm).HasColumnName("aprovado_em");
            u.Property(x => x.CriadoEm).HasColumnName("criado_em");
            u.Property(x => x.AtualizadoEm).HasColumnName("atualizado_em");
            u.Ignore(x => x.Exige2FA);
            u.Ignore(x => x.Tem2FA);
            u.Ignore(x => x.PodeEntrarComChave);
        });

        modelBuilder.Entity<Convite>(c =>
        {
            c.ToTable("convite_usuario");
            c.HasKey(x => x.Id);
            c.Property(x => x.Id).HasColumnName("id");
            c.Property(x => x.UsuarioId).HasColumnName("usuario_id");
            c.Property(x => x.TokenHash).HasColumnName("token_hash");
            c.Property(x => x.ExpiraEm).HasColumnName("expira_em");
            c.Property(x => x.UsadoEm).HasColumnName("usado_em");
            c.Property(x => x.CriadoPor).HasColumnName("criado_por");
            c.Property(x => x.CriadoEm).HasColumnName("criado_em");
            // As chaves estrangeiras declaradas fazem o EF gravar o usuário antes do convite.
            c.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId);
            c.HasOne<Usuario>().WithMany().HasForeignKey(x => x.CriadoPor).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ChaveDeAcesso>(c =>
        {
            c.ToTable("chave_de_acesso");
            c.HasKey(x => x.Id);
            c.Property(x => x.Id).HasColumnName("id");
            c.Property(x => x.UsuarioId).HasColumnName("usuario_id");
            c.Property(x => x.CredencialId).HasColumnName("credencial_id");
            c.Property(x => x.ChavePublica).HasColumnName("chave_publica");
            c.Property(x => x.Contador).HasColumnName("contador");
            c.Property(x => x.Nome).HasColumnName("nome");
            c.Property(x => x.CriadaEm).HasColumnName("criada_em");
            c.Property(x => x.UltimoUsoEm).HasColumnName("ultimo_uso_em");
            c.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId);
        });
    }

    /// <summary>RNF05: registra entrar, errar a senha, sair e mudanças de acesso. A tabela só aceita acréscimo.</summary>
    public Task AuditarAsync(Guid usuarioId, string acao, IPAddress? ip, CancellationToken ct) =>
        Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO retirada.auditoria_acesso (usuario_id, acao, ip) VALUES ({usuarioId}, {acao}, {ip})", ct);
}
