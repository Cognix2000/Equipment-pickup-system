using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Retirada.Comum;
using Shouldly;

namespace Retirada.Api.Testes;

/// <summary>RN14: fotos da retirada contra a API, o banco e o armazenamento (pasta, como no desenvolvimento).</summary>
[Collection(ColecaoDaApi.Nome)]
public class FotosTestes(ApiDeTeste api)
{
    private static readonly object GpsNoCliente = new { latitude = -20.1234, longitude = -45.5678, precisaoM = 8 };

    private static Task<HttpResponseMessage> RetirarAsync(HttpClient cliente, OrdemDeTeste o, object[] fotos, string? serieDaOnu = null)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, $"/api/ordens/{o.Id}/retirada")
        {
            Content = JsonContent.Create(new
            {
                itens = new object[]
                {
                    new { equipamentoId = o.OnuId, numeroSerie = serieDaOnu ?? o.SerieDaOnu },
                    new { equipamentoId = o.FonteId, numeroSerie = (string?)null },
                },
                gps = GpsNoCliente,
                fotos,
            }),
        };
        pedido.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return cliente.SendAsync(pedido);
    }

    private static object Foto(Guid id, string tipo = "equipamento", Guid? equipamentoId = null) =>
        new { id, tipo, equipamentoId, capturadaEm = new DateTimeOffset(2026, 10, 15, 11, 30, 0, TimeSpan.FromHours(-3)) };

    private static async Task DeveRecusarAsync(Task<HttpResponseMessage> envio, HttpStatusCode status, string? codigo = null)
    {
        var resposta = await envio;
        var corpo = await resposta.Content.ReadAsStringAsync();
        resposta.StatusCode.ShouldBe(status, corpo);
        if (codigo is not null) JsonDocument.Parse(corpo).RootElement.GetProperty("codigo").GetString().ShouldBe(codigo);
    }

    private async Task<(HttpClient Tecnico, OrdemDeTeste Ordem)> OrdemComTecnicoAsync()
    {
        var (tecnico, usuario) = await api.EntrarComoAsync(Papeis.Tecnico);
        var ordem = await api.ImportarOrdemAsync((o, agora) => { o.Liberar(agora); o.Designar(usuario.Id, agora); });
        return (tecnico, ordem);
    }

    [FatoComBanco]
    public async Task Foto_sem_EXIF_fica_ligada_ao_evento_da_retirada()
    {
        var (tecnico, ordem) = await OrdemComTecnicoAsync();
        var (financeiro, _) = await api.EntrarComoAsync(Papeis.Financeiro);

        var envio = await tecnico.PostAsync($"/api/ordens/{ordem.Id}/fotos", Formulario(JpegDeTeste.Montar(1600, 1200)));
        envio.StatusCode.ShouldBe(HttpStatusCode.OK, await envio.Content.ReadAsStringAsync());
        var enviada = JsonDocument.Parse(await envio.Content.ReadAsStringAsync()).RootElement;
        var foto = enviada.GetProperty("id").GetGuid();
        enviada.GetProperty("largura").GetInt32().ShouldBe(1600);

        var resposta = await RetirarAsync(tecnico, ordem, [Foto(foto)]);
        var corpo = await resposta.Content.ReadAsStringAsync();
        resposta.StatusCode.ShouldBe(HttpStatusCode.OK, corpo);
        var noDetalhe = JsonDocument.Parse(corpo).RootElement.GetProperty("fotos").EnumerateArray().Single();
        noDetalhe.GetProperty("id").GetGuid().ShouldBe(foto);
        noDetalhe.GetProperty("tipo").GetString().ShouldBe("equipamento");

        // A evidência aponta para o evento da retirada e guarda o hash do arquivo limpo.
        (await api.EscalarAsync<bool>("""
            SELECT e.evento_id = (SELECT id FROM retirada.evento_ordem WHERE ordem_id = @ordem AND tipo = 'retirada')
                   AND e.mime = 'image/jpeg' AND e.largura = 1600 AND e.altura = 1200 AND e.local IS NOT NULL
              FROM retirada.evidencia e WHERE e.id = @foto
            """, ("ordem", ordem.Id), ("foto", foto))).ShouldBeTrue();
        var sha = await api.EscalarAsync<string>("SELECT sha256 FROM retirada.evidencia WHERE id = @foto", ("foto", foto));

        // O técnico e o financeiro veem a foto, sem EXIF e igual ao hash guardado.
        foreach (var quem in new[] { tecnico, financeiro })
        {
            var arquivo = await quem.GetAsync($"/api/ordens/{ordem.Id}/fotos/{foto}");
            arquivo.StatusCode.ShouldBe(HttpStatusCode.OK);
            arquivo.Content.Headers.ContentType!.MediaType.ShouldBe("image/jpeg");
            var bytes = await arquivo.Content.ReadAsByteArrayAsync();
            Encoding.ASCII.GetString(bytes).ShouldNotContain("GPS");
            Convert.ToHexStringLower(SHA256.HashData(bytes)).ShouldBe(sha);
        }

        // A pendente some depois que a retirada vale; a definitiva fica.
        Directory.GetFiles(ApiDeTeste.PastaDasFotos, $"{foto:N}.jpg", SearchOption.AllDirectories)
            .ShouldHaveSingleItem().ShouldContain($"ordens{Path.DirectorySeparatorChar}{ordem.Id:N}");
    }

    [FatoComBanco]
    public async Task Retirada_sem_foto_ou_com_foto_que_nao_chegou_e_recusada()
    {
        var (tecnico, ordem) = await OrdemComTecnicoAsync();

        await DeveRecusarAsync(RetirarAsync(tecnico, ordem, []), HttpStatusCode.UnprocessableEntity, "RN14");
        await DeveRecusarAsync(RetirarAsync(tecnico, ordem, [Foto(Guid.NewGuid())]), HttpStatusCode.UnprocessableEntity, "RN14");

        // Foto enviada por outro técnico não serve, mesmo com o id certo.
        var (outro, outroUsuario) = await api.EntrarComoAsync(Papeis.Tecnico);
        var ordemDoOutro = await api.ImportarOrdemAsync((o, agora) => { o.Liberar(agora); o.Designar(outroUsuario.Id, agora); });
        var fotoDoOutro = await ApiDeTeste.FotoEnviadaAsync(outro, ordemDoOutro.Id);
        await DeveRecusarAsync(RetirarAsync(tecnico, ordem, [Foto(fotoDoOutro)]), HttpStatusCode.UnprocessableEntity, "RN14");

        (await api.EscalarAsync<string>("SELECT status FROM retirada.ordem WHERE id = @id", ("id", ordem.Id))).ShouldBe("atribuida");
        (await api.EscalarAsync<long>("SELECT count(*) FROM retirada.evidencia WHERE ordem_id = @id", ("id", ordem.Id))).ShouldBe(0);
    }

    [FatoComBanco]
    public async Task Serie_divergente_vai_com_a_foto_da_etiqueta()
    {
        var (tecnico, ordem) = await OrdemComTecnicoAsync();
        var conjunto = await ApiDeTeste.FotoEnviadaAsync(tecnico, ordem.Id);

        await DeveRecusarAsync(RetirarAsync(tecnico, ordem, [Foto(conjunto)], serieDaOnu: "OUTRASERIE01"), HttpStatusCode.UnprocessableEntity, "RN14");

        var etiqueta = await ApiDeTeste.FotoEnviadaAsync(tecnico, ordem.Id);
        var resposta = await RetirarAsync(tecnico, ordem, [Foto(conjunto), Foto(etiqueta, "etiqueta_serie", ordem.OnuId)], serieDaOnu: "OUTRASERIE01");
        resposta.StatusCode.ShouldBe(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        (await api.EscalarAsync<Guid>("SELECT equipamento_id FROM retirada.evidencia WHERE id = @id", ("id", etiqueta))).ShouldBe(ordem.OnuId);
    }

    [FatoComBanco]
    public async Task Envio_recusa_o_que_nao_e_JPEG_e_a_ordem_de_outro()
    {
        var (tecnico, ordem) = await OrdemComTecnicoAsync();
        var (outro, _) = await api.EntrarComoAsync(Papeis.Tecnico);
        var (financeiro, _) = await api.EntrarComoAsync(Papeis.Financeiro);

        await DeveRecusarAsync(tecnico.PostAsync($"/api/ordens/{ordem.Id}/fotos", Formulario("%PDF-1.7"u8.ToArray())), HttpStatusCode.UnprocessableEntity, "RN14");
        await DeveRecusarAsync(ApiDeTeste.EnviarFotoAsync(outro, ordem.Id), HttpStatusCode.NotFound);
        await DeveRecusarAsync(ApiDeTeste.EnviarFotoAsync(financeiro, ordem.Id), HttpStatusCode.Forbidden); // só o técnico fotografa
    }

    [FatoComBanco]
    public async Task Foto_de_ordem_alheia_nao_aparece_para_outro_tecnico() // RNF04
    {
        var (tecnico, ordem) = await OrdemComTecnicoAsync();
        var foto = await ApiDeTeste.FotoEnviadaAsync(tecnico, ordem.Id);
        (await RetirarAsync(tecnico, ordem, [Foto(foto)])).StatusCode.ShouldBe(HttpStatusCode.OK);
        var (outro, _) = await api.EntrarComoAsync(Papeis.Tecnico);

        (await outro.GetAsync($"/api/ordens/{ordem.Id}/fotos/{foto}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await tecnico.GetAsync($"/api/ordens/{Guid.NewGuid()}/fotos/{foto}")).StatusCode.ShouldBe(HttpStatusCode.NotFound); // foto de outra ordem
    }

    private static MultipartFormDataContent Formulario(byte[] conteudo)
    {
        var formulario = new MultipartFormDataContent();
        formulario.Add(new ByteArrayContent(conteudo), "foto", "foto.jpg");
        return formulario;
    }
}
