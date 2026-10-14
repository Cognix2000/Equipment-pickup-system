using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OtpNet;
using Retirada.Acesso.Dominio;
using Retirada.Acesso.Infraestrutura;
using Retirada.Acesso.Seguranca;
using Retirada.Banco;
using Retirada.Comum;
using Retirada.Ordens.Dominio;
using Retirada.Ordens.Infraestrutura;
using Shouldly;

namespace Retirada.Api.Testes;

/// <summary>Com banco de TESTE em RETIRADA_TESTE_BANCO; sem a variável, os testes ficam ignorados.</summary>
public sealed class FatoComBancoAttribute : FactAttribute
{
    public FatoComBancoAttribute()
    {
        if (string.IsNullOrWhiteSpace(ApiDeTeste.Conexao)) Skip = "Defina RETIRADA_TESTE_BANCO para rodar os testes com banco.";
    }
}

/// <summary>A API inteira, de verdade, contra o PostgreSQL de teste.</summary>
public sealed class ApiDeTeste : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static string? Conexao => Environment.GetEnvironmentVariable("RETIRADA_TESTE_BANCO");
    public const string Senha = "senha de teste bem comprida";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Retirada", Conexao);
        builder.UseSetting("Acesso:TentativasPorMinuto", "10000");
        builder.UseSetting("Acesso:RequisicoesPorMinuto", "10000");
        builder.UseSetting("Acesso:RequisicoesAnonimasPorMinuto", "10000");
        builder.UseSetting("Importacao:ModoSimulacao", "true"); // telefones fictícios com DDD 00
        builder.UseSetting("DataProtection:Pasta", Path.Combine(Path.GetTempPath(), "retirada-chaves-testes"));
    }

    public async Task InitializeAsync()
    {
        if (Conexao is not null) await Migrador.AplicarAsync(Conexao, null, TextWriter.Null);
    }

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    public HttpClient NovoCliente()
    {
        var cliente = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        cliente.DefaultRequestHeaders.Add("X-Retirada", "1");
        return cliente;
    }

    /// <summary>Cria um usuário ativo, com senha, direto no banco.</summary>
    public async Task<Usuario> CriarUsuarioAsync(string papel)
    {
        await using var db = new AcessoDbContext(new DbContextOptionsBuilder<AcessoDbContext>().UseNpgsql(Conexao).Options);
        var usuario = Usuario.CadastrarAutoaprovado($"Teste {papel}", $"{papel}.{Guid.NewGuid():N}@exemplo.invalid", papel, DateTimeOffset.UtcNow);
        usuario.DefinirSenha(HashDeSenha.Gerar(Senha), DateTimeOffset.UtcNow);
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync();
        return usuario;
    }

    public static async Task<string> EtapaAsync(HttpResponseMessage resposta) =>
        (await resposta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("etapa").GetString()!;

    public static Task<HttpResponseMessage> EntrarAsync(HttpClient cliente, string email, string senha = Senha) =>
        cliente.PostAsJsonAsync("/api/acesso/entrar", new { email, senha });

    /// <summary>Gestor e financeiro: senha, QR code e primeiro código do aplicativo.</summary>
    public async Task<HttpClient> EntrarCom2FAAsync(Usuario usuario)
    {
        var cliente = NovoCliente();
        var resposta = await EntrarAsync(cliente, usuario.Email);
        (await EtapaAsync(resposta)).ShouldBe("configurar");

        var qr = await (await cliente.PostAsync("/api/acesso/2fa/iniciar", null)).Content.ReadFromJsonAsync<JsonElement>();
        var codigo = new Totp(Base32Encoding.ToBytes(qr.GetProperty("segredo").GetString())).ComputeTotp();
        var confirmado = await cliente.PostAsJsonAsync("/api/acesso/2fa/confirmar", new { codigo });
        confirmado.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await EtapaAsync(confirmado)).ShouldBe("pronto");
        return cliente;
    }

    /// <summary>Cria um usuário do papel e devolve a sessão dele já pronta (com 2FA quando o papel exige).</summary>
    public async Task<(HttpClient Cliente, Usuario Usuario)> EntrarComoAsync(string papel)
    {
        var usuario = await CriarUsuarioAsync(papel);
        if (usuario.Exige2FA) return (await EntrarCom2FAAsync(usuario), usuario);

        var cliente = NovoCliente();
        (await EtapaAsync(await EntrarAsync(cliente, usuario.Email))).ShouldBe("pronto");
        return (cliente, usuario);
    }

    /// <summary>
    /// Importa uma ordem fictícia com uma ONU (com série) e uma fonte, pelo próprio agregado.
    /// <paramref name="preparar"/> leva a ordem ao estado que o teste precisa (liberar, designar...).
    /// </summary>
    public async Task<OrdemDeTeste> ImportarOrdemAsync(Action<Ordem, DateTimeOffset>? preparar = null)
    {
        var clienteId = Guid.NewGuid();
        await using (var conexao = new NpgsqlConnection(Conexao))
        {
            await conexao.OpenAsync();
            await using var comando = new NpgsqlCommand(
                "INSERT INTO retirada.cliente (id, nome, tipo_pessoa) VALUES (@id, 'CLIENTE TESTE COMANDOS', 'PF')", conexao);
            comando.Parameters.AddWithValue("id", clienteId);
            await comando.ExecuteNonQueryAsync();
        }

        var serie = $"SIMU{Guid.NewGuid():N}"[..16].ToUpperInvariant();
        var agora = DateTimeOffset.UtcNow;
        var ordem = Ordem.Importar(
            $"teste-{Guid.NewGuid():N}", 7001, "REMOÇÃO EQUIP FTTH", clienteId,
            new Endereco("RUA DAS ACÁCIAS", "45", null, "CENTRO", "CIDADE SIMULADA", "MG", null),
            null, null,
            [Equipamento.Criar(TipoEquipamento.ONU, "Modelo Teste", NumeroSerie.Criar(serie)), Equipamento.Criar(TipoEquipamento.Fonte, "Fonte 12V", null)],
            agora, TimeSpan.FromHours(48));
        preparar?.Invoke(ordem, agora);

        await using var db = new OrdensDbContext(new DbContextOptionsBuilder<OrdensDbContext>()
            .UseNpgsql(Conexao, o => o.UseNetTopologySuite()).Options);
        db.Ordens.Add(ordem);
        await db.SaveChangesAsync();

        var onu = ordem.Equipamentos.Single(e => e.Tipo == TipoEquipamento.ONU).Id;
        var fonte = ordem.Equipamentos.Single(e => e.Tipo == TipoEquipamento.Fonte).Id;
        return new OrdemDeTeste(ordem.Id, onu, serie, fonte);
    }

    /// <summary>Eventos gravados para a ordem, na ordem em que aconteceram.</summary>
    public async Task<List<(int Seq, string Tipo, string? Motivo, Guid? Ator, bool ComLocal)>> EventosAsync(Guid ordemId)
    {
        await using var conexao = new NpgsqlConnection(Conexao);
        await conexao.OpenAsync();
        await using var comando = new NpgsqlCommand(
            "SELECT seq, tipo, motivo, ator_id, local IS NOT NULL FROM retirada.evento_ordem WHERE ordem_id = @id ORDER BY seq", conexao);
        comando.Parameters.AddWithValue("id", ordemId);
        await using var leitor = await comando.ExecuteReaderAsync();
        var eventos = new List<(int, string, string?, Guid?, bool)>();
        while (await leitor.ReadAsync())
            eventos.Add((leitor.GetInt32(0), leitor.GetString(1), leitor.IsDBNull(2) ? null : leitor.GetString(2),
                leitor.IsDBNull(3) ? null : leitor.GetGuid(3), leitor.GetBoolean(4)));
        return eventos;
    }

    /// <summary>Primeira coluna da primeira linha de uma consulta ao banco de teste.</summary>
    public async Task<T?> EscalarAsync<T>(string sql, params (string Nome, object Valor)[] parametros)
    {
        await using var conexao = new NpgsqlConnection(Conexao);
        await conexao.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexao);
        foreach (var (nome, valor) in parametros) comando.Parameters.AddWithValue(nome, valor);
        var resultado = await comando.ExecuteScalarAsync();
        return resultado is null or DBNull ? default : (T)resultado;
    }

    public async Task<Guid> CriarOrdemAsync(Guid? responsavel)
    {
        var id = Guid.NewGuid();
        var cliente = Guid.NewGuid();
        await using var conexao = new NpgsqlConnection(Conexao);
        await conexao.OpenAsync();
        await using var comando = new NpgsqlCommand("""
            INSERT INTO retirada.cliente (id, nome, tipo_pessoa) VALUES (@cliente, 'CLIENTE TESTE ACESSO', 'PF');
            INSERT INTO retirada.ordem (id, chave_importacao, tipo_os, cliente_id, logradouro, bairro, cidade, uf, status, carencia_ate, responsavel_id)
            VALUES (@id, @chave, 'REMOÇÃO EQUIP FTTH', @cliente, 'RUA', 'CENTRO', 'CIDADE', 'MG', 'atribuida', now(), @responsavel);
            """, conexao);
        comando.Parameters.AddWithValue("id", id);
        comando.Parameters.AddWithValue("cliente", cliente);
        comando.Parameters.AddWithValue("chave", $"acesso-{id:N}");
        comando.Parameters.AddWithValue("responsavel", (object?)responsavel ?? DBNull.Value);
        await comando.ExecuteNonQueryAsync();
        return id;
    }
}

public sealed record OrdemDeTeste(Guid Id, Guid OnuId, string SerieDaOnu, Guid FonteId);

/// <summary>
/// Uma API de teste para todas as classes que usam o banco: elas rodam em sequência
/// e as migrações são aplicadas uma vez só (em paralelo, duas aplicações colidiam).
/// </summary>
[CollectionDefinition(Nome)]
public sealed class ColecaoDaApi : ICollectionFixture<ApiDeTeste>
{
    public const string Nome = "API com banco";
}
