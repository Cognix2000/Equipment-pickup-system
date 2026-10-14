using System.Net.NetworkInformation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NetTopologySuite.Geometries;
using Retirada.Ordens.Dominio;

namespace Retirada.Ordens.Infraestrutura;

/// <summary>
/// Contexto do módulo Ordens. Só mapeia as tabelas que o módulo usa; o esquema é criado
/// pelas migrações de Retirada.Banco, nunca por este contexto.
/// </summary>
public sealed class OrdensDbContext(DbContextOptions<OrdensDbContext> options) : DbContext(options)
{
    public const string Esquema = "retirada";

    public DbSet<Ordem> Ordens => Set<Ordem>();
    public DbSet<ClienteLeitura> Clientes => Set<ClienteLeitura>();
    public DbSet<UsuarioLeitura> Usuarios => Set<UsuarioLeitura>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Esquema);
        ConfigurarOrdem(modelBuilder.Entity<Ordem>());
        ConfigurarEquipamento(modelBuilder.Entity<Equipamento>());

        modelBuilder.Entity<ClienteLeitura>(c =>
        {
            c.ToTable("cliente");
            c.HasKey(x => x.Id);
            c.Property(x => x.Id).HasColumnName("id");
            c.Property(x => x.Nome).HasColumnName("nome");
            c.Property(x => x.CodigoHubsoft).HasColumnName("codigo_hubsoft");
        });

        modelBuilder.Entity<UsuarioLeitura>(u =>
        {
            u.ToTable("usuario");
            u.HasKey(x => x.Id);
            u.Property(x => x.Id).HasColumnName("id");
            u.Property(x => x.Papel).HasColumnName("papel");
            u.Property(x => x.Situacao).HasColumnName("situacao");
        });
    }

    private static void ConfigurarOrdem(EntityTypeBuilder<Ordem> o)
    {
        o.ToTable("ordem");
        o.HasKey(x => x.Id);
        o.Property(x => x.Id).HasColumnName("id");
        o.Property(x => x.ChaveImportacao).HasColumnName("chave_importacao");
        o.Property(x => x.NumeroOs).HasColumnName("numero_os");
        o.Property(x => x.TipoOs).HasColumnName("tipo_os");
        o.Property(x => x.ClienteId).HasColumnName("cliente_id");
        o.Property(x => x.Observacao).HasColumnName("observacao");
        o.Property(x => x.Status).HasColumnName("status").HasConversion(TextoSnake<StatusOrdem>());
        o.Property(x => x.CarenciaAte).HasColumnName("carencia_ate");
        o.Property(x => x.ResponsavelId).HasColumnName("responsavel_id");
        o.Property(x => x.VisitasSemSucesso).HasColumnName("visitas_sem_sucesso");
        o.Property(x => x.Versao).HasColumnName("versao").IsConcurrencyToken();
        o.Property(x => x.CriadoEm).HasColumnName("criado_em");
        o.Property(x => x.AtualizadoEm).HasColumnName("atualizado_em");
        o.Property(x => x.EncerradaEm).HasColumnName("encerrada_em");
        o.Property(x => x.BaixaHubsoftEm).HasColumnName("baixa_hubsoft_em");
        o.Property(x => x.BaixaHubsoftPor).HasColumnName("baixa_hubsoft_por");
        o.Property(x => x.Protocolo).HasColumnName("protocolo");
        o.Property(x => x.IdOrdemServicoHubsoft).HasColumnName("id_ordem_servico_hubsoft");
        o.Property(x => x.IdClienteServicoHubsoft).HasColumnName("id_cliente_servico_hubsoft");
        o.Property(x => x.DataProgramada).HasColumnName("data_programada");
        o.Property(x => x.DataRescisao).HasColumnName("data_rescisao");
        o.Property(x => x.Cep).HasColumnName("cep");
        o.Property(x => x.ImportacaoId).HasColumnName("importacao_id");

        o.Property(x => x.Localizacao)
            .HasColumnName("localizacao")
            .HasColumnType("geography (point, 4326)")
            .HasConversion(
                l => l == null ? null : new Point(l.Longitude, l.Latitude) { SRID = 4326 },
                p => p == null ? null : new Localizacao(p.Y, p.X));

        o.ComplexProperty(x => x.Endereco, e =>
        {
            e.Property(x => x.Logradouro).HasColumnName("logradouro");
            e.Property(x => x.Numero).HasColumnName("numero");
            e.Property(x => x.Complemento).HasColumnName("complemento");
            e.Property(x => x.Bairro).HasColumnName("bairro");
            e.Property(x => x.Cidade).HasColumnName("cidade");
            e.Property(x => x.Uf).HasColumnName("uf");
            e.Property(x => x.Referencia).HasColumnName("referencia");
        });

        o.HasMany(x => x.Equipamentos).WithOne().HasForeignKey("OrdemId");
        o.Navigation(x => x.Equipamentos).HasField("_equipamentos").UsePropertyAccessMode(PropertyAccessMode.Field);
        o.Ignore(x => x.Eventos);
        o.Ignore(x => x.AguardaBaixaNoHubsoft);
    }

    private static void ConfigurarEquipamento(EntityTypeBuilder<Equipamento> e)
    {
        e.ToTable("equipamento_ordem");
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id");
        e.Property<Guid>("OrdemId").HasColumnName("ordem_id");
        e.Property(x => x.Tipo).HasColumnName("tipo").HasConversion(
            t => t == TipoEquipamento.RepetidorMesh ? "Repetidor Mesh" : t.ToString(),
            s => s == "Repetidor Mesh" ? TipoEquipamento.RepetidorMesh : Enum.Parse<TipoEquipamento>(s));
        e.Property(x => x.Modelo).HasColumnName("modelo");
        e.Property(x => x.NumeroSerie).HasColumnName("numero_serie").HasConversion(
            n => n == null ? null : n.Valor,
            s => s == null ? null : NumeroSerie.Criar(s));
        e.Property(x => x.Situacao).HasColumnName("situacao").HasConversion(TextoSnake<SituacaoEquipamento>());
        e.Property(x => x.Mac).HasColumnName("mac").HasConversion(
            m => m == null ? null : PhysicalAddress.Parse(m),
            p => p == null ? null : string.Join(':', p.GetAddressBytes().Select(b => b.ToString("X2"))));
        e.Property(x => x.Valor).HasColumnName("valor");
        e.Property(x => x.ConferidoPor).HasColumnName("conferido_por");
        e.Property(x => x.ConferidoEm).HasColumnName("conferido_em");
        e.Ignore(x => x.EhPatrimonio);
    }

    /// <summary>Enum em C# (EmRota) ↔ texto do banco (em_rota).</summary>
    private static ValueConverter<T, string> TextoSnake<T>() where T : struct, Enum =>
        new(v => ParaSnake(v.ToString()), s => Enum.Parse<T>(s.Replace("_", string.Empty), true));

    internal static string ParaSnake(string nome) =>
        string.Concat(nome.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
}

/// <summary>Leitura do cliente para listas. Quem grava cliente é a importação.</summary>
public sealed class ClienteLeitura
{
    public Guid Id { get; private set; }
    public string Nome { get; private set; } = string.Empty;
    public int? CodigoHubsoft { get; private set; }
}

/// <summary>Leitura do usuário para validar quem recebe a ordem. Quem grava usuário é o módulo Acesso.</summary>
public sealed class UsuarioLeitura
{
    public Guid Id { get; private set; }
    public string Papel { get; private set; } = string.Empty;
    public string Situacao { get; private set; } = string.Empty;
}
