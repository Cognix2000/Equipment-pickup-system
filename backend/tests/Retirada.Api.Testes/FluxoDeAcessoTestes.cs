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

namespace Retirada.Api.Testes;

[Collection(ColecaoDaApi.Nome)]
public class FluxoDeAcessoTestes(ApiDeTeste api)
{
    private static Task<string> EtapaAsync(HttpResponseMessage resposta) => ApiDeTeste.EtapaAsync(resposta);

    private static Task<HttpResponseMessage> EntrarAsync(HttpClient cliente, string email, string senha = ApiDeTeste.Senha) =>
        ApiDeTeste.EntrarAsync(cliente, email, senha);

    private Task<HttpClient> EntrarCom2FAAsync(Usuario usuario) => api.EntrarCom2FAAsync(usuario);

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
    public async Task Financeiro_sem_2FA_entra_direto_e_pode_ligar_o_2FA_depois() // ADR-015
    {
        var financeiro = await api.CriarUsuarioAsync(Papeis.Financeiro);
        var cliente = api.NovoCliente();

        // 2FA opcional: o primeiro login do financeiro vem "pronto" direto, sem passar por "configurar".
        (await EtapaAsync(await EntrarAsync(cliente, financeiro.Email))).ShouldBe("pronto");
        (await cliente.GetFromJsonAsync<JsonElement>("/api/acesso/eu")).GetProperty("etapa").GetString().ShouldBe(EtapasDeAcesso.Completo);

        // E tem acesso às ordens, como qualquer financeiro logado.
        (await cliente.GetAsync("/api/ordens")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Mesmo logado, pode ligar o 2FA depois pelo painel (/api/acesso/2fa/iniciar aceita a etapa Completo).
        (await cliente.PostAsync("/api/acesso/2fa/iniciar", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
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
