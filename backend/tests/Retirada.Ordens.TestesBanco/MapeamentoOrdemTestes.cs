using Microsoft.EntityFrameworkCore;
using Npgsql;
using Retirada.Banco;
using Retirada.Ordens.Dominio;
using Retirada.Ordens.Infraestrutura;
using Shouldly;

namespace Retirada.Ordens.TestesBanco;

/// <summary>
/// Confere o mapeamento do EF Core contra o esquema real (CHECKs, tipos, PostGIS).
/// Precisa de um PostgreSQL com PostGIS: defina RETIRADA_TESTE_BANCO com a conexão do dono
/// de um banco de TESTE. Sem a variável, os testes aparecem como ignorados.
/// </summary>
public sealed class FatoComBancoAttribute : FactAttribute
{
    public FatoComBancoAttribute()
    {
        if (string.IsNullOrWhiteSpace(BancoDeTeste.Conexao))
            Skip = "Defina RETIRADA_TESTE_BANCO para rodar os testes com banco.";
    }
}

public sealed class BancoDeTeste : IAsyncLifetime
{
    public static string? Conexao => Environment.GetEnvironmentVariable("RETIRADA_TESTE_BANCO");

    public Guid ClienteId { get; } = Guid.CreateVersion7();

    public async Task InitializeAsync()
    {
        if (Conexao is null) return;
        await Migrador.AplicarAsync(Conexao, null, TextWriter.Null);

        await using var conexao = new NpgsqlConnection(Conexao);
        await conexao.OpenAsync();
        await using var comando = new NpgsqlCommand(
            "INSERT INTO retirada.cliente (id, nome, tipo_pessoa) VALUES (@id, 'CLIENTE TESTE AUTOMATICO', 'PF')", conexao);
        comando.Parameters.AddWithValue("id", ClienteId);
        await comando.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public OrdensDbContext NovoContexto() =>
        new(new DbContextOptionsBuilder<OrdensDbContext>()
            .UseNpgsql(Conexao, o => o.UseNetTopologySuite())
            .Options);
}

public class MapeamentoOrdemTestes(BancoDeTeste banco) : IClassFixture<BancoDeTeste>
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private Ordem NovaOrdem() => Ordem.Importar(
        $"teste-{Guid.NewGuid():N}", 9001, "REMOÇÃO EQUIP FTTH", banco.ClienteId,
        new Endereco("RUA DOS IPÊS", "120", null, "CENTRO", "CIDADE SIMULADA", "MG", null),
        new Localizacao(-21.0012, -38.9978), null,
        [
            Equipamento.Criar(TipoEquipamento.RepetidorMesh, "Modelo Teste", NumeroSerie.Criar($"SIMU{Random.Shared.Next():X8}")),
            Equipamento.Criar(TipoEquipamento.Fonte, "Fonte 12V", null),
        ],
        Agora, TimeSpan.FromHours(48));

    [FatoComBanco]
    public async Task Grava_e_le_a_ordem_com_endereco_localizacao_e_equipamentos()
    {
        var ordem = NovaOrdem();
        await using (var db = banco.NovoContexto())
        {
            db.Ordens.Add(ordem);
            await db.SaveChangesAsync();
        }

        await using var leitura = banco.NovoContexto();
        var lida = await leitura.Ordens.Include(o => o.Equipamentos).SingleAsync(o => o.Id == ordem.Id);

        lida.Status.ShouldBe(StatusOrdem.Nova);
        lida.Endereco.Bairro.ShouldBe("CENTRO");
        lida.Localizacao!.Latitude.ShouldBe(-21.0012, 0.000001);
        lida.Equipamentos.Select(e => e.Tipo).ShouldBe([TipoEquipamento.RepetidorMesh, TipoEquipamento.Fonte], ignoreOrder: true);
    }

    [FatoComBanco]
    public async Task Ciclo_ate_Conferida_respeita_os_CHECKs_do_banco()
    {
        var ordem = NovaOrdem();
        await using var db = banco.NovoContexto();
        db.Ordens.Add(ordem);
        await db.SaveChangesAsync();

        var tecnico = await TecnicoAsync();
        ordem.Liberar(Agora);
        ordem.Designar(tecnico, Agora);
        var series = ordem.Equipamentos.ToDictionary(e => e.Id, e => e.NumeroSerie);
        ordem.RegistrarRetirada(series, new Localizacao(-21.0012, -38.9978), Agora);
        ordem.Conferir(series, tecnico, Agora).Fechou.ShouldBeTrue();
        await db.SaveChangesAsync();

        var status = await db.Database.SqlQuery<string>($"SELECT status AS \"Value\" FROM retirada.ordem WHERE id = {ordem.Id}").SingleAsync();
        status.ShouldBe("conferida");
    }

    [FatoComBanco]
    public async Task Dois_aceites_ao_mesmo_tempo_so_um_vence()
    {
        var ordem = NovaOrdem();
        ordem.Liberar(Agora);
        await using (var db = banco.NovoContexto())
        {
            db.Ordens.Add(ordem);
            await db.SaveChangesAsync();
        }

        var tecnico = await TecnicoAsync();
        await using var primeiro = banco.NovoContexto();
        await using var segundo = banco.NovoContexto();
        var a = await primeiro.Ordens.SingleAsync(o => o.Id == ordem.Id);
        var b = await segundo.Ordens.SingleAsync(o => o.Id == ordem.Id);

        a.Designar(tecnico, Agora);
        b.Designar(tecnico, Agora);
        await primeiro.SaveChangesAsync();

        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => segundo.SaveChangesAsync());
    }

    private async Task<Guid> TecnicoAsync()
    {
        var id = Guid.CreateVersion7();
        await using var conexao = new NpgsqlConnection(BancoDeTeste.Conexao);
        await conexao.OpenAsync();
        await using var comando = new NpgsqlCommand(
            "INSERT INTO retirada.usuario (id, nome, email, papel, situacao) VALUES (@id, 'Técnico Teste', @email, 'tecnico', 'ativo')", conexao);
        comando.Parameters.AddWithValue("id", id);
        comando.Parameters.AddWithValue("email", $"tecnico.{id:N}@exemplo.invalid");
        await comando.ExecuteNonQueryAsync();
        return id;
    }
}
