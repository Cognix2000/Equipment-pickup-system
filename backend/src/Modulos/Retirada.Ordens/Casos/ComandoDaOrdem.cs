global using RespostaDeComando = Microsoft.AspNetCore.Http.HttpResults.Results<
    Microsoft.AspNetCore.Http.HttpResults.Ok<Retirada.Ordens.Casos.OrdemDto>,
    Microsoft.AspNetCore.Http.HttpResults.NotFound,
    Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>;

using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Retirada.Comum;
using Retirada.Ordens.Dominio;
using Retirada.Ordens.Infraestrutura;

namespace Retirada.Ordens.Casos;

/// <summary>Parâmetros das regras que o gestor ajusta sem mexer no código (RF18).</summary>
/// <param name="ModoSimulacao">Homologação com dados fictícios: aceita o DDD 00 (RN05).</param>
/// <param name="DiasSemResposta">Dias depois da carência sem resposta do cliente antes de encerrar (RN09).</param>
internal sealed record ParametrosDasOrdens(
    int LimiteDeVisitas, int CarenciaHoras = 48, bool ModoSimulacao = false, int DiasSemResposta = 10, int ValorPorRetiradaCentavos = 500);

/// <summary>O que todo comando recebe: a ordem da rota, a chave de idempotência e o contexto da requisição.</summary>
internal sealed record PedidoDeComando(
    Guid Id,
    [FromHeader(Name = ComandoDaOrdem.CabecalhoDeIdempotencia)] Guid? ChaveDeIdempotencia,
    OrdensDbContext Db,
    ClaimsPrincipal Usuario,
    TimeProvider Relogio,
    CancellationToken Ct);

/// <summary>GPS do aparelho no momento da ação (RF12, RF13).</summary>
internal sealed record Gps(double Latitude, double Longitude, double? PrecisaoM)
{
    public Localizacao Localizacao() => new(Latitude, Longitude);
}

/// <summary>
/// Caminho comum de todos os comandos da ordem (ADR-009):
/// <list type="number">
/// <item>Chave de idempotência obrigatória. Reenvio da mesma chave devolve o estado atual sem repetir a ação
/// (o celular reenvia quando o sinal volta).</item>
/// <item>Técnico só mexe na ordem que está com ele; a de outro responde 404 (RNF04).</item>
/// <item>A regra fica no agregado: violação vira 422 com o código da regra, pelo tratador global.</item>
/// <item>Ordem e evento são gravados na mesma transação. A versão da ordem barra duas ações ao mesmo tempo:
/// a segunda recebe 409 (concorrência otimista).</item>
/// </list>
/// </summary>
internal static class ComandoDaOrdem
{
    public const string CabecalhoDeIdempotencia = "Idempotency-Key";

    /// <param name="acao">Aplica a regra no agregado e devolve o que vai para <c>evento_ordem.dados</c>.</param>
    /// <param name="gravarJunto">O que mais o comando grava, na mesma transação da ordem e do evento.</param>
    /// <param name="depoisDeGravar">Arrumação depois do commit (ex.: apagar arquivo temporário). Falha aqui não desfaz o comando.</param>
    public static async Task<RespostaDeComando> ExecutarAsync(
        PedidoDeComando p, Func<Ordem, DateTimeOffset, Task<object?>> acao, Gps? gps = null,
        Func<Ordem, DateTimeOffset, Task>? gravarJunto = null, Func<Task>? depoisDeGravar = null)
    {
        if (p.ChaveDeIdempotencia is not Guid chave || chave == Guid.Empty)
            return Problema(StatusCodes.Status400BadRequest, "IDEMPOTENCIA",
                $"Envie o cabeçalho {CabecalhoDeIdempotencia} com um UUID novo para cada ação.");

        var usuarioId = p.Usuario.Id();
        var anterior = await p.Db.Database
            .SqlQuery<EventoAnterior>($"SELECT ordem_id AS \"OrdemId\", ator_id AS \"AtorId\" FROM retirada.evento_ordem WHERE chave_idempotencia = {chave}")
            .SingleOrDefaultAsync(p.Ct);
        if (anterior is not null)
        {
            if (anterior.OrdemId != p.Id || anterior.AtorId != usuarioId)
                return Problema(StatusCodes.Status422UnprocessableEntity, "IDEMPOTENCIA",
                    "Essa chave já foi usada em outra ação. Gere uma nova.");
            // Reenvio: a ação já foi feita. Devolve como está agora, sem repetir.
            return TypedResults.Ok((await Projecoes.CarregarOrdemAsync(p.Db, p.Id, p.Ct))!);
        }

        var ordem = await p.Db.Ordens.Include(o => o.Equipamentos).SingleOrDefaultAsync(o => o.Id == p.Id, p.Ct);
        if (ordem is null || (p.Usuario.EhTecnico() && !await EstaComOTecnicoAsync(p, ordem, usuarioId))) return TypedResults.NotFound();

        var statusDe = ordem.Status;
        var agora = p.Relogio.GetUtcNow();
        var dados = await acao(ordem, agora);
        var evento = ordem.Eventos.Single(); // cada comando do agregado gera exatamente um evento

        await using var transacao = await p.Db.Database.BeginTransactionAsync(p.Ct);
        try
        {
            await p.Db.SaveChangesAsync(p.Ct);
            var autor = new AutorDoEvento(usuarioId, p.Usuario.Papel(), gps?.Latitude, gps?.Longitude, gps?.PrecisaoM);
            await RegistroDeEventos.GravarAsync(p.Db, ordem.Id, evento, statusDe, ordem.Status, autor, chave, dados, p.Ct);
            if (gravarJunto is not null) await gravarJunto(ordem, agora);
            await transacao.CommitAsync(p.Ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflito();
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation && e.ConstraintName == "evento_ordem_chave_idempotencia_key")
        {
            return Conflito(); // o mesmo reenvio chegou duas vezes ao mesmo tempo; o próximo cai no caminho do reenvio
        }

        if (depoisDeGravar is not null)
        {
            try { await depoisDeGravar(); }
            catch (Exception) when (!p.Ct.IsCancellationRequested) { /* só arrumação: o comando já valeu */ }
        }

        return TypedResults.Ok((await Projecoes.CarregarOrdemAsync(p.Db, p.Id, p.Ct))!);
    }

    /// <summary>Séries lidas pelo técnico ou pelo estoque, uma por equipamento da ordem.</summary>
    public static Dictionary<Guid, NumeroSerie?> SeriesLidas(Ordem ordem, IReadOnlyList<ItemLido> itens, string regra)
    {
        var daOrdem = ordem.Equipamentos.Select(e => e.Id).ToHashSet();
        if (itens.Any(i => !daOrdem.Contains(i.EquipamentoId)))
            throw new RegraDeNegocioException(regra, "Há item que não é desta ordem.");
        if (itens.GroupBy(i => i.EquipamentoId).Any(g => g.Count() > 1))
            throw new RegraDeNegocioException(regra, "O mesmo item foi enviado duas vezes.");

        return itens.ToDictionary(
            i => i.EquipamentoId,
            i => string.IsNullOrWhiteSpace(i.NumeroSerie) ? null : NumeroSerie.Criar(i.NumeroSerie));
    }

    public static Dictionary<string, string?> ParaRegistro(Dictionary<Guid, NumeroSerie?> series) =>
        series.ToDictionary(s => s.Key.ToString(), s => s.Value?.Valor);

    /// <summary>
    /// RNF04: técnico só age na ordem que está com ele. A exceção é a ordem cancelada que era dele:
    /// o cancelamento tira o responsável, mas o app precisa do 422 RN07 para avisar (UC04), não de um 404.
    /// </summary>
    private static async Task<bool> EstaComOTecnicoAsync(PedidoDeComando p, Ordem ordem, Guid tecnicoId)
    {
        if (ordem.ResponsavelId == tecnicoId) return true;
        if (ordem.Status != StatusOrdem.Cancelada) return false;
        var tecnico = tecnicoId.ToString();
        return await p.Db.Database
            .SqlQuery<bool>($"SELECT EXISTS (SELECT 1 FROM retirada.evento_ordem WHERE ordem_id = {ordem.Id} AND tipo = 'designada' AND dados ->> 'tecnicoId' = {tecnico}) AS \"Value\"")
            .SingleAsync(p.Ct);
    }

    private static ProblemHttpResult Conflito() => Problema(StatusCodes.Status409Conflict, "CONFLITO",
        "Outra ação mudou essa ordem agora há pouco. Atualize a tela e tente de novo.");

    private static ProblemHttpResult Problema(int status, string codigo, string titulo) =>
        TypedResults.Problem(title: titulo, statusCode: status, extensions: new Dictionary<string, object?> { ["codigo"] = codigo });

    internal sealed record EventoAnterior(Guid OrdemId, Guid? AtorId);
}

/// <summary>Configuração comum dos endpoints de comando: permissão, resumo e respostas de erro no contrato.</summary>
internal static class ConfiguracaoDeComando
{
    public static RouteHandlerBuilder ComoComando(this RouteHandlerBuilder rota, string politica, string resumo) => rota
        .RequireAuthorization(politica)
        .WithSummary(resumo)
        .WithDescription($"Exige o cabeçalho {ComandoDaOrdem.CabecalhoDeIdempotencia} com um UUID novo por ação; reenviar a mesma chave devolve a ordem sem repetir a ação.")
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
}

/// <summary>Série lida (ou digitada) de um equipamento. Fonte vai sem série.</summary>
internal sealed record ItemLido(Guid EquipamentoId, string? NumeroSerie);
