using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Retirada.Ordens.Infraestrutura;

namespace Retirada.Ordens.Rotinas;

/// <summary>Configuração das rotinas automáticas (Ordens:Rotinas no appsettings ou Ordens__Rotinas__* no ambiente).</summary>
internal sealed record ParametrosDasRotinas(TimeSpan Intervalo);

/// <summary>
/// Roda as rotinas automáticas das ordens dentro da própria API, a cada <see cref="ParametrosDasRotinas.Intervalo"/>.
/// Erro numa rodada vai para o log e a rotina tenta de novo na próxima; a API segue no ar.
/// </summary>
internal sealed partial class RotinasDasOrdens(
    IServiceScopeFactory escopos, TimeProvider relogio, ParametrosDasRotinas parametros, ILogger<RotinasDasOrdens> log)
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
        try
        {
            await using var escopo = escopos.CreateAsyncScope();
            var db = escopo.ServiceProvider.GetRequiredService<OrdensDbContext>();

            // Lote cheio: pode haver mais vencidas (ex.: a API ficou parada). Repete até esvaziar.
            int total = 0, rodada;
            do
            {
                rodada = await LiberacaoPelaCarencia.ExecutarAsync(db, relogio.GetUtcNow(), ct);
                total += rodada;
                db.ChangeTracker.Clear();
            } while (rodada == LiberacaoPelaCarencia.Lote);

            if (total > 0) OrdensLiberadas(total);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // a API está parando
        }
        catch (Exception erro)
        {
            RotinaFalhou(erro);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Fim da carência: {Quantidade} ordem(ns) ficaram disponíveis (RN03).")]
    private partial void OrdensLiberadas(int quantidade);

    [LoggerMessage(Level = LogLevel.Error, Message = "A rotina das ordens falhou; tenta de novo na próxima rodada.")]
    private partial void RotinaFalhou(Exception erro);
}
