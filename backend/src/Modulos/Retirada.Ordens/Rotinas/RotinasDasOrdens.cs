using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Retirada.Ordens.Casos;
using Retirada.Ordens.Infraestrutura;

namespace Retirada.Ordens.Rotinas;

/// <summary>Configuração das rotinas automáticas (Ordens:Rotinas no appsettings ou Ordens__Rotinas__* no ambiente).</summary>
internal sealed record ParametrosDasRotinas(TimeSpan Intervalo);

/// <summary>
/// Roda as rotinas automáticas das ordens dentro da própria API, a cada <see cref="ParametrosDasRotinas.Intervalo"/>.
/// Erro numa rodada vai para o log e a rotina tenta de novo na próxima; a API segue no ar.
/// </summary>
internal sealed partial class RotinasDasOrdens(
    IServiceScopeFactory escopos, TimeProvider relogio, ParametrosDasRotinas parametros, ParametrosDasOrdens regras,
    ILogger<RotinasDasOrdens> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var temporizador = new PeriodicTimer(parametros.Intervalo, relogio);
        do
        {
            await RodarAsync(ct);
        } while (await temporizador.WaitForNextTickAsync(ct));
    }

    private async Task RodarAsync(CancellationToken ct)
    {
        // Cada rotina falha sozinha: um erro numa não impede a outra de rodar.
        await RodarAsync("carência", (db, agora) => LiberacaoPelaCarencia.ExecutarAsync(db, agora, ct), OrdensLiberadas, ct);
        var prazo = TimeSpan.FromDays(regras.DiasSemResposta);
        await RodarAsync("sem resposta", (db, agora) => EncerramentoSemResposta.ExecutarAsync(db, prazo, agora, ct), OrdensEncerradas, ct);
    }

    private async Task RodarAsync(string rotina, Func<OrdensDbContext, DateTimeOffset, Task<int>> lote, Action<int> registrar, CancellationToken ct)
    {
        try
        {
            await using var escopo = escopos.CreateAsyncScope();
            var db = escopo.ServiceProvider.GetRequiredService<OrdensDbContext>();

            // Lote cheio: pode haver mais (ex.: a API ficou parada). Repete até esvaziar.
            int total = 0, rodada;
            do
            {
                rodada = await lote(db, relogio.GetUtcNow());
                total += rodada;
                db.ChangeTracker.Clear();
            } while (rodada == Lote.Tamanho);

            if (total > 0) registrar(total);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // a API está parando
        }
        catch (Exception erro)
        {
            RotinaFalhou(erro, rotina);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Fim da carência: {Quantidade} ordem(ns) ficaram disponíveis (RN03).")]
    private partial void OrdensLiberadas(int quantidade);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sem resposta no prazo: {Quantidade} ordem(ns) encerradas sem retirada (RN09).")]
    private partial void OrdensEncerradas(int quantidade);

    [LoggerMessage(Level = LogLevel.Error, Message = "A rotina das ordens ({Rotina}) falhou; tenta de novo na próxima rodada.")]
    private partial void RotinaFalhou(Exception erro, string rotina);
}
