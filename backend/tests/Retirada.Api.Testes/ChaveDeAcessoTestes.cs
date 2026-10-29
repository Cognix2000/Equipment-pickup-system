using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Retirada.Acesso.Dominio;
using Retirada.Comum;
using Shouldly;

namespace Retirada.Api.Testes;

/// <summary>ADR-012: chave de acesso (passkey) no lugar do aplicativo autenticador, com o WebAuthn verificado de verdade.</summary>
[Collection(ColecaoDaApi.Nome)]
public class ChaveDeAcessoTestes(ApiDeTeste api)
{
    private static async Task<JsonElement> OpcoesAsync(HttpClient cliente, string caminho)
    {
        var resposta = await cliente.PostAsync(caminho, null);
        resposta.StatusCode.ShouldBe(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        return await resposta.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<HttpResponseMessage> CadastrarAsync(HttpClient cliente, AutenticadorDeTeste aparelho, string nome = "Celular")
    {
        var opcoes = await OpcoesAsync(cliente, "/api/acesso/chave/cadastro/opcoes");
        return await cliente.PostAsJsonAsync("/api/acesso/chave/cadastro", new { credencial = aparelho.Cadastrar(opcoes), nome });
    }

    private static async Task<HttpResponseMessage> EntrarComChaveAsync(HttpClient cliente, AutenticadorDeTeste aparelho, ECDsa? outraChave = null)
    {
        var opcoes = await OpcoesAsync(cliente, "/api/acesso/chave/entrar/opcoes");
        return await cliente.PostAsJsonAsync("/api/acesso/chave/entrar", new { credencial = aparelho.Assinar(opcoes, outraChave) });
    }

    private static async Task<string?> CodigoDoErroAsync(HttpResponseMessage resposta) =>
        (await resposta.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("codigo", out var c) ? c.GetString() : null;

    /// <summary>Primeiro acesso do financeiro: senha e, no lugar do QR code, a chave do aparelho.</summary>
    private async Task<(Usuario Usuario, AutenticadorDeTeste Aparelho)> FinanceiroComChaveAsync()
    {
        var financeiro = await api.CriarUsuarioAsync(Papeis.Financeiro);
        var cliente = api.NovoCliente();
        (await ApiDeTeste.EtapaAsync(await ApiDeTeste.EntrarAsync(cliente, financeiro.Email))).ShouldBe("configurar");

        var aparelho = new AutenticadorDeTeste();
        var cadastro = await CadastrarAsync(cliente, aparelho);
        cadastro.StatusCode.ShouldBe(HttpStatusCode.OK, await cadastro.Content.ReadAsStringAsync());
        (await ApiDeTeste.EtapaAsync(cadastro)).ShouldBe("pronto");
        return (financeiro, aparelho);
    }

    [FatoComBanco]
    public async Task Financeiro_troca_o_aplicativo_pela_chave_e_entra_so_com_ela()
    {
        var (_, aparelho) = await FinanceiroComChaveAsync();

        var cliente = api.NovoCliente();
        var entrada = await EntrarComChaveAsync(cliente, aparelho);
        entrada.StatusCode.ShouldBe(HttpStatusCode.OK, await entrada.Content.ReadAsStringAsync());
        (await ApiDeTeste.EtapaAsync(entrada)).ShouldBe("pronto");

        (await cliente.GetFromJsonAsync<JsonElement>("/api/acesso/eu")).GetProperty("etapa").GetString().ShouldBe(EtapasDeAcesso.Completo);
        (await cliente.GetAsync("/api/ordens")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [FatoComBanco]
    public async Task Com_a_senha_quem_tem_chave_e_mandado_confirmar_com_ela_sem_abrir_sessao()
    {
        var (financeiro, _) = await FinanceiroComChaveAsync();
        var cliente = api.NovoCliente();

        (await ApiDeTeste.EtapaAsync(await ApiDeTeste.EntrarAsync(cliente, financeiro.Email))).ShouldBe("chave");
        (await cliente.GetAsync("/api/acesso/eu")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [FatoComBanco]
    public async Task Resposta_assinada_serve_uma_vez_so()
    {
        var (_, aparelho) = await FinanceiroComChaveAsync();
        var cliente = api.NovoCliente();
        var opcoes = await OpcoesAsync(cliente, "/api/acesso/chave/entrar/opcoes");
        var assinada = aparelho.Assinar(opcoes);

        (await cliente.PostAsJsonAsync("/api/acesso/chave/entrar", new { credencial = assinada })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var outro = api.NovoCliente();
        var repetida = await outro.PostAsJsonAsync("/api/acesso/chave/entrar", new { credencial = assinada });
        (await CodigoDoErroAsync(repetida)).ShouldBe("CHAVE_EXPIRADA"); // sem o desafio guardado

        await OpcoesAsync(outro, "/api/acesso/chave/entrar/opcoes"); // desafio novo, resposta velha
        var deOutroDesafio = await outro.PostAsJsonAsync("/api/acesso/chave/entrar", new { credencial = assinada });
        deOutroDesafio.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodigoDoErroAsync(deOutroDesafio)).ShouldBe("CHAVE_RECUSADA");
    }

    [FatoComBanco]
    public async Task Assinatura_de_outra_chave_privada_e_recusada()
    {
        var (_, aparelho) = await FinanceiroComChaveAsync();
        using var falsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var resposta = await EntrarComChaveAsync(api.NovoCliente(), aparelho, falsa);

        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodigoDoErroAsync(resposta)).ShouldBe("CHAVE_RECUSADA");
    }

    [FatoComBanco]
    public async Task Sem_digital_rosto_ou_PIN_a_chave_nao_vale_como_segundo_fator()
    {
        var (_, aparelho) = await FinanceiroComChaveAsync();
        var cliente = api.NovoCliente();
        var opcoes = await OpcoesAsync(cliente, "/api/acesso/chave/entrar/opcoes");

        var resposta = await cliente.PostAsJsonAsync("/api/acesso/chave/entrar", new { credencial = aparelho.Assinar(opcoes, comDigital: false) });

        resposta.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodigoDoErroAsync(resposta)).ShouldBe("CHAVE_RECUSADA");
    }

    [FatoComBanco]
    public async Task Chave_de_outro_endereco_e_recusada() // phishing: o site falso não consegue usar a chave
    {
        var (sessao, _) = await api.EntrarComoAsync(Papeis.Tecnico);
        using var noSiteFalso = new AutenticadorDeTeste("https://netwise-retirada.exemplo.invalid");

        var resposta = await CadastrarAsync(sessao, noSiteFalso);

        resposta.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodigoDoErroAsync(resposta)).ShouldBe("CHAVE_RECUSADA");
    }

    [FatoComBanco]
    public async Task Na_etapa_do_codigo_nao_da_para_trocar_o_aplicativo_por_uma_chave()
    {
        var gestor = await api.CriarUsuarioAsync(Papeis.Gestor);
        await api.EntrarCom2FAAsync(gestor); // ativa o aplicativo
        var cliente = api.NovoCliente();
        (await ApiDeTeste.EtapaAsync(await ApiDeTeste.EntrarAsync(cliente, gestor.Email))).ShouldBe("codigo");

        (await cliente.PostAsync("/api/acesso/chave/cadastro/opcoes", null)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [FatoComBanco]
    public async Task Tecnico_cadastra_lista_e_remove_a_propria_chave()
    {
        var (cliente, _) = await api.EntrarComoAsync(Papeis.Tecnico);
        using var aparelho = new AutenticadorDeTeste();

        (await CadastrarAsync(cliente, aparelho, "Celular da empresa")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CadastrarAsync(cliente, aparelho)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity); // a mesma chave de novo

        var chaves = await cliente.GetFromJsonAsync<JsonElement>("/api/acesso/chaves");
        chaves.GetArrayLength().ShouldBe(1);
        chaves[0].GetProperty("nome").GetString().ShouldBe("Celular da empresa");
        var id = chaves[0].GetProperty("id").GetGuid();

        var (doOutro, _) = await api.EntrarComoAsync(Papeis.Tecnico);
        (await doOutro.PostAsync($"/api/acesso/chaves/{id}/remover", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await cliente.PostAsync($"/api/acesso/chaves/{id}/remover", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await cliente.GetFromJsonAsync<JsonElement>("/api/acesso/chaves")).GetArrayLength().ShouldBe(0);
        (await CodigoDoErroAsync(await EntrarComChaveAsync(api.NovoCliente(), aparelho))).ShouldBe("CHAVE_DESCONHECIDA");
    }

    [FatoComBanco]
    public async Task Revogado_nao_entra_com_a_chave_e_zerar_o_2FA_apaga_as_chaves() // RF02
    {
        var (financeiro, aparelho) = await FinanceiroComChaveAsync();
        var (gestor, _) = await api.EntrarComoAsync(Papeis.Gestor);

        var lista = await gestor.GetFromJsonAsync<JsonElement>("/api/usuarios");
        lista.EnumerateArray().Single(u => u.GetProperty("id").GetGuid() == financeiro.Id).GetProperty("doisFatores").GetBoolean().ShouldBeTrue();

        (await gestor.PostAsync($"/api/usuarios/{financeiro.Id}/revogar", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var revogado = await EntrarComChaveAsync(api.NovoCliente(), aparelho);
        revogado.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodigoDoErroAsync(revogado)).ShouldBe("ACESSO_INATIVO");

        (await gestor.PostAsync($"/api/usuarios/{financeiro.Id}/reativar", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var zerado = await gestor.PostAsync($"/api/usuarios/{financeiro.Id}/zerar-2fa", null);
        (await zerado.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("doisFatores").GetBoolean().ShouldBeFalse();

        (await CodigoDoErroAsync(await EntrarComChaveAsync(api.NovoCliente(), aparelho))).ShouldBe("CHAVE_DESCONHECIDA");
        (await ApiDeTeste.EtapaAsync(await ApiDeTeste.EntrarAsync(api.NovoCliente(), financeiro.Email))).ShouldBe("configurar");
    }

    [FatoComBanco]
    public async Task Gestor_com_aplicativo_pode_entrar_tambem_com_a_chave()
    {
        var gestor = await api.CriarUsuarioAsync(Papeis.Gestor);
        var sessao = await api.EntrarCom2FAAsync(gestor);
        using var aparelho = new AutenticadorDeTeste();
        (await CadastrarAsync(sessao, aparelho)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Com a senha continua pedindo o código; a chave entra direto.
        (await ApiDeTeste.EtapaAsync(await ApiDeTeste.EntrarAsync(api.NovoCliente(), gestor.Email))).ShouldBe("codigo");
        (await ApiDeTeste.EtapaAsync(await EntrarComChaveAsync(api.NovoCliente(), aparelho))).ShouldBe("pronto");
    }
}
