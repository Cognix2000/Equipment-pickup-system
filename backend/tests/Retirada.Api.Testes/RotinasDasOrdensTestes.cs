using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Retirada.Ordens.Infraestrutura;
using Retirada.Ordens.Rotinas;
using Shouldly;

namespace Retirada.Api.Testes;

/// <summary>Rotinas automáticas das ordens contra o banco de verdade. A API de teste não roda a rotina sozinha.</summary>
[Collection(ColecaoDaApi.Nome)]
public class RotinasDasOrdensTestes(ApiDeTeste api)
{
    private async Task<int> RodarAsync()
    {
        await using var escopo = api.Services.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<OrdensDbContext>();
        return await LiberacaoPelaCarencia.ExecutarAsync(db, DateTimeOffset.UtcNow, CancellationToken.None);
    }

    private Task VencerCarenciaAsync(Guid ordem) =>
        api.EscalarAsync<object>("UPDATE retirada.ordem SET carencia_ate = now() - interval '1 minute' WHERE id = @id", ("id", ordem));

    private Task<string?> StatusAsync(Guid ordem) =>
        api.EscalarAsync<string>("SELECT status FROM retirada.ordem WHERE id = @id", ("id", ordem));

    [FatoComBanco]
    public async Task Carencia_vencida_fica_disponivel_com_evento_do_sistema() // RN03
    {
        var vencida = await api.ImportarOrdemAsync();
        var emCarencia = await api.ImportarOrdemAsync();
        await VencerCarenciaAsync(vencida.Id);

        (await RodarAsync()).ShouldBeGreaterThanOrEqualTo(1);

        (await StatusAsync(vencida.Id)).ShouldBe("disponivel");
        (await StatusAsync(emCarencia.Id)).ShouldBe("nova");

        var evento = (await api.EventosAsync(vencida.Id)).ShouldHaveSingleItem();
        evento.Tipo.ShouldBe("carencia_vencida");
        evento.Ator.ShouldBeNull(); // ator_id NULL = o próprio sistema
        (await api.EscalarAsync<string>("SELECT ator_papel FROM retirada.evento_ordem WHERE ordem_id = @id", ("id", vencida.Id))).ShouldBe("sistema");
        (await api.EscalarAsync<int>("SELECT versao FROM retirada.ordem WHERE id = @id", ("id", vencida.Id))).ShouldBe(2);
        (await api.EventosAsync(emCarencia.Id)).ShouldBeEmpty();
    }

    [FatoComBanco]
    public async Task Com_a_rotina_ligada_a_API_libera_sozinha_ao_subir()
    {
        var ordem = await api.ImportarOrdemAsync();
        await VencerCarenciaAsync(ordem.Id);

        await using var comRotina = api.WithWebHostBuilder(b => b.UseSetting("Ordens:Rotinas:Ativas", "true"));
        using var _ = comRotina.CreateClient(); // sobe o host; a primeira rodada sai na partida

        for (var tentativa = 0; tentativa < 50 && await StatusAsync(ordem.Id) == "nova"; tentativa++)
            await Task.Delay(200);
        (await StatusAsync(ordem.Id)).ShouldBe("disponivel");
    }

    [FatoComBanco]
    public async Task Rodar_de_novo_nao_repete_a_liberacao()
    {
        var ordem = await api.ImportarOrdemAsync();
        await VencerCarenciaAsync(ordem.Id);

        await RodarAsync();
        await RodarAsync();

        (await api.EventosAsync(ordem.Id)).ShouldHaveSingleItem();
    }

    [FatoComBanco]
    public async Task Ordem_travada_por_outra_acao_fica_para_a_proxima_rodada()
    {
        var ordem = await api.ImportarOrdemAsync();
        await VencerCarenciaAsync(ordem.Id);

        // Outra transação (ex.: o financeiro liberando na mão) segura a linha enquanto a rotina passa.
        await using (var conexao = new NpgsqlConnection(ApiDeTeste.Conexao))
        {
            await conexao.OpenAsync();
            await using var transacao = await conexao.BeginTransactionAsync();
            await using (var trava = new NpgsqlCommand("SELECT 1 FROM retirada.ordem WHERE id = @id FOR UPDATE", conexao, transacao))
            {
                trava.Parameters.AddWithValue("id", ordem.Id);
                await trava.ExecuteScalarAsync();
            }

            await RodarAsync(); // não espera a trava nem falha: pula a ordem
            (await StatusAsync(ordem.Id)).ShouldBe("nova");
        }

        await RodarAsync();
        (await StatusAsync(ordem.Id)).ShouldBe("disponivel");
    }
}
