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
using Shouldly;

namespace Retirada.Acesso.Testes;

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

public class FluxoDeAcessoTestes(ApiDeTeste api) : IClassFixture<ApiDeTeste>
{
    private static async Task<string> EtapaAsync(HttpResponseMessage resposta) =>
        (await resposta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("etapa").GetString()!;

    private static Task<HttpResponseMessage> EntrarAsync(HttpClient cliente, string email, string senha = ApiDeTeste.Senha) =>
        cliente.PostAsJsonAsync("/api/acesso/entrar", new { email, senha });

    /// <summary>Gestor e financeiro: senha, QR code e primeiro código do aplicativo.</summary>
    private async Task<HttpClient> EntrarCom2FAAsync(Usuario usuario)
    {
        var cliente = api.NovoCliente();
        var resposta = await EntrarAsync(cliente, usuario.Email);
        (await EtapaAsync(resposta)).ShouldBe("configurar");

        var qr = await (await cliente.PostAsync("/api/acesso/2fa/iniciar", null)).Content.ReadFromJsonAsync<JsonElement>();
        var codigo = new Totp(Base32Encoding.ToBytes(qr.GetProperty("segredo").GetString())).ComputeTotp();
        var confirmado = await cliente.PostAsJsonAsync("/api/acesso/2fa/confirmar", new { codigo });
        confirmado.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await EtapaAsync(confirmado)).ShouldBe("pronto");
        return cliente;
    }

    [FatoComBanco]
    public async Task Sem_login_a_API_responde_401()
    {
        var resposta = await api.NovoCliente().GetAsync("/api/ordens");
        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [FatoComBanco]
    public async Task Comando_sem_o_cabecalho_do_aplicativo_e_barrado()
    {
        var cliente = api.CreateClient();
        var resposta = await EntrarAsync(cliente, "qualquer@exemplo.invalid");
        resposta.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [FatoComBanco]
    public async Task Tecnico_entra_e_so_ve_as_proprias_ordens() // RF01 e RNF04
    {
        var tecnico = await api.CriarUsuarioAsync(Papeis.Tecnico);
        var dele = await api.CriarOrdemAsync(tecnico.Id);
        var deOutro = await api.CriarOrdemAsync(null);
        var cliente = api.NovoCliente();

        (await EtapaAsync(await EntrarAsync(cliente, tecnico.Email))).ShouldBe("pronto");

        var lista = await cliente.GetFromJsonAsync<JsonElement>("/api/ordens");
        lista.EnumerateArray().Select(o => o.GetProperty("id").GetGuid()).ShouldBe([dele]);
        (await cliente.GetAsync($"/api/ordens/{deOutro}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await cliente.GetAsync($"/api/ordens/{dele}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [FatoComBanco]
    public async Task Tecnico_nao_acessa_a_gestao_de_usuarios()
    {
        var tecnico = await api.CriarUsuarioAsync(Papeis.Tecnico);
        var cliente = api.NovoCliente();
        await EntrarAsync(cliente, tecnico.Email);

        (await cliente.GetAsync("/api/usuarios")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [FatoComBanco]
    public async Task Senha_errada_cinco_vezes_bloqueia_mesmo_com_a_senha_certa() // RNF03
    {
        var tecnico = await api.CriarUsuarioAsync(Papeis.Tecnico);
        var cliente = api.NovoCliente();

        for (var i = 0; i < 5; i++)
            (await EntrarAsync(cliente, tecnico.Email, "senha errada qualquer")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await EntrarAsync(cliente, tecnico.Email)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [FatoComBanco]
    public async Task Email_inexistente_e_senha_errada_dao_a_mesma_resposta()
    {
        var tecnico = await api.CriarUsuarioAsync(Papeis.Tecnico);
        var cliente = api.NovoCliente();

        var inexistente = await (await EntrarAsync(cliente, "ninguem@exemplo.invalid")).Content.ReadAsStringAsync();
        var errada = await (await EntrarAsync(cliente, tecnico.Email, "senha errada qualquer")).Content.ReadAsStringAsync();

        JsonDocument.Parse(inexistente).RootElement.GetProperty("title").GetString()
            .ShouldBe(JsonDocument.Parse(errada).RootElement.GetProperty("title").GetString());
    }

    [FatoComBanco]
    public async Task Financeiro_sem_2FA_nao_passa_da_etapa_de_configurar() // RNF03
    {
        var financeiro = await api.CriarUsuarioAsync(Papeis.Financeiro);
        var cliente = api.NovoCliente();

        (await EtapaAsync(await EntrarAsync(cliente, financeiro.Email))).ShouldBe("configurar");

        (await cliente.GetAsync("/api/ordens")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await cliente.GetFromJsonAsync<JsonElement>("/api/acesso/eu")).GetProperty("etapa").GetString().ShouldBe(EtapasDeAcesso.Configurar);
    }

    [FatoComBanco]
    public async Task Gestor_convida_tecnico_e_revogar_corta_o_acesso_na_hora() // RF02
    {
        var gestor = await EntrarCom2FAAsync(await api.CriarUsuarioAsync(Papeis.Gestor));

        var cadastro = await gestor.PostAsJsonAsync("/api/usuarios",
            new { nome = "Técnico Convidado", email = $"convidado.{Guid.NewGuid():N}@exemplo.invalid", papel = Papeis.Tecnico });
        cadastro.StatusCode.ShouldBe(HttpStatusCode.Created, await cadastro.Content.ReadAsStringAsync());
        var corpo = await cadastro.Content.ReadFromJsonAsync<JsonElement>();
        var idTecnico = corpo.GetProperty("usuario").GetProperty("id").GetGuid();
        var email = corpo.GetProperty("usuario").GetProperty("email").GetString()!;
        corpo.GetProperty("usuario").GetProperty("situacao").GetString().ShouldBe("pendente");
        var token = corpo.GetProperty("convite").GetProperty("caminho").GetString()!.Split("#t=")[1];

        var tecnico = api.NovoCliente();
        (await EntrarAsync(tecnico, email)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized); // pendente ainda não entra
        (await tecnico.PostAsJsonAsync("/api/acesso/convite", new { token, senha = "curta" })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await tecnico.PostAsJsonAsync("/api/acesso/convite", new { token, senha = ApiDeTeste.Senha })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await tecnico.PostAsJsonAsync("/api/acesso/convite", new { token, senha = ApiDeTeste.Senha })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        (await EtapaAsync(await EntrarAsync(tecnico, email))).ShouldBe("pronto");
        (await tecnico.GetAsync("/api/ordens")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await gestor.PostAsync($"/api/usuarios/{idTecnico}/revogar", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await tecnico.GetAsync("/api/ordens")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await EntrarAsync(tecnico, email)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [FatoComBanco]
    public async Task Sair_encerra_a_sessao()
    {
        var tecnico = await api.CriarUsuarioAsync(Papeis.Tecnico);
        var cliente = api.NovoCliente();
        await EntrarAsync(cliente, tecnico.Email);

        (await cliente.PostAsync("/api/acesso/sair", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await cliente.GetAsync("/api/acesso/eu")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [FatoComBanco]
    public async Task Entrar_e_errar_ficam_na_auditoria() // RNF05
    {
        var tecnico = await api.CriarUsuarioAsync(Papeis.Tecnico);
        var cliente = api.NovoCliente();
        await EntrarAsync(cliente, tecnico.Email, "senha errada qualquer");
        await EntrarAsync(cliente, tecnico.Email);

        await using var conexao = new NpgsqlConnection(ApiDeTeste.Conexao);
        await conexao.OpenAsync();
        await using var comando = new NpgsqlCommand("SELECT string_agg(acao, ',' ORDER BY id) FROM retirada.auditoria_acesso WHERE usuario_id = @id", conexao);
        comando.Parameters.AddWithValue("id", tecnico.Id);
        (await comando.ExecuteScalarAsync()).ShouldBe("login_falhou,login");
    }
}
