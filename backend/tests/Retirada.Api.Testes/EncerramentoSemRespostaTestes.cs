using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Retirada.Comum;
using Retirada.Ordens.Infraestrutura;
using Retirada.Ordens.Rotinas;
using Shouldly;

namespace Retirada.Api.Testes;

/// <summary>RN09: encerramento depois de 10 dias sem resposta, contra o banco de verdade.</summary>
[Collection(ColecaoDaApi.Nome)]
public class EncerramentoSemRespostaTestes(ApiDeTeste api)
{
    private static readonly TimeSpan Prazo = TimeSpan.FromDays(10);

    private async Task RodarAsync()
    {
        await using var escopo = api.Services.CreateAsyncScope();
        var db = escopo.ServiceProvider.GetRequiredService<OrdensDbContext>();
        await EncerramentoSemResposta.ExecutarAsync(db, Prazo, DateTimeOffset.UtcNow, CancellationToken.None);
    }

    /// <summary>Ordem Disponível cuja carência venceu há <paramref name="dias"/> dias.</summary>
    private async Task<Guid> DisponivelHaAsync(int dias)
    {
        var ordem = await api.ImportarOrdemAsync((o, agora) => o.Liberar(agora));
        await api.EscalarAsync<object>("UPDATE retirada.ordem SET carencia_ate = now() - make_interval(days => @dias) WHERE id = @id",
            ("dias", dias), ("id", ordem.Id));
        return ordem.Id;
    }

    /// <summary>Tentativas gravadas direto no banco, num telefone fictício (DDD 00) do cliente da ordem.</summary>
    private async Task ContatarAsync(Guid ordem, Guid autor, params string[] resultados)
    {
        await using var conexao = new NpgsqlConnection(ApiDeTeste.Conexao);
        await conexao.OpenAsync();
        await using var comando = new NpgsqlCommand("""
            WITH tel AS (
                INSERT INTO retirada.telefone_cliente (cliente_id, numero_e164, como_veio, tipo, formato_valido)
                SELECT cliente_id, '+5500991110000', '00 99111-0000', 'celular', true FROM retirada.ordem WHERE id = @ordem
                RETURNING id)
            INSERT INTO retirada.tentativa_contato (ordem_id, telefone_id, canal, resultado, feito_por)
            SELECT @ordem, tel.id, 'ligacao', r, @autor FROM tel, unnest(@resultados) AS r
            """, conexao);
        comando.Parameters.AddWithValue("ordem", ordem);
        comando.Parameters.AddWithValue("autor", autor);
        comando.Parameters.AddWithValue("resultados", resultados);
        await comando.ExecuteNonQueryAsync();
    }

    private Task<string?> StatusAsync(Guid ordem) =>
        api.EscalarAsync<string>("SELECT status FROM retirada.ordem WHERE id = @id", ("id", ordem));

    [FatoComBanco]
    public async Task Com_tentativas_e_sem_resposta_encerra_e_vai_para_a_cobranca()
    {
        var financeiro = await api.CriarUsuarioAsync(Papeis.Financeiro);
        var ordem = await DisponivelHaAsync(11);
        await ContatarAsync(ordem, financeiro.Id, "nao_atendeu", "caixa_postal");

        await RodarAsync();

        (await StatusAsync(ordem)).ShouldBe("encerrada_sem_retirada");
        (await api.EscalarAsync<bool>("SELECT encerrada_em IS NOT NULL FROM retirada.ordem WHERE id = @id", ("id", ordem))).ShouldBeTrue();
        var evento = (await api.EventosAsync(ordem)).ShouldHaveSingleItem();
        evento.Tipo.ShouldBe("encerrada_sem_resposta");
        evento.Ator.ShouldBeNull(); // o próprio sistema
    }

    [FatoComBanco]
    public async Task Nao_encerra_antes_do_prazo_nem_quem_respondeu_nem_quem_nunca_foi_procurado()
    {
        var financeiro = await api.CriarUsuarioAsync(Papeis.Financeiro);
        var antesDoPrazo = await DisponivelHaAsync(9);
        await ContatarAsync(antesDoPrazo, financeiro.Id, "nao_atendeu");
        var respondeu = await DisponivelHaAsync(30);
        await ContatarAsync(respondeu, financeiro.Id, "nao_atendeu", "atendeu", "nao_atendeu");
        var semTentativa = await DisponivelHaAsync(30);

        await RodarAsync();

        (await StatusAsync(antesDoPrazo)).ShouldBe("disponivel");
        (await StatusAsync(respondeu)).ShouldBe("disponivel");
        (await StatusAsync(semTentativa)).ShouldBe("disponivel");
        (await api.EventosAsync(semTentativa)).ShouldBeEmpty();
    }

    [FatoComBanco]
    public async Task Lista_marca_a_ordem_sem_contato_no_prazo_para_o_financeiro()
    {
        var (financeiro, usuario) = await api.EntrarComoAsync(Papeis.Financeiro);
        var semTentativa = await DisponivelHaAsync(11);
        var comTentativa = await DisponivelHaAsync(5);
        await ContatarAsync(comTentativa, usuario.Id, "nao_atendeu");
        var noPrazo = await DisponivelHaAsync(2);

        var lista = JsonDocument.Parse(await financeiro.GetStringAsync("/api/ordens?status=disponivel")).RootElement.EnumerateArray().ToList();
        bool? Marcada(Guid id) => lista.Single(o => o.GetProperty("id").GetGuid() == id).GetProperty("semContatoNoPrazo").GetBoolean();

        Marcada(semTentativa).ShouldBe(true);
        Marcada(comTentativa).ShouldBe(false);
        Marcada(noPrazo).ShouldBe(false);
    }
}
