using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Retirada.Comum;
using Retirada.Ordens.Dominio;
using Retirada.Ordens.Importacao;
using Retirada.Ordens.Infraestrutura;

namespace Retirada.Ordens.Casos;

/// <summary>
/// RF03 (estendido): o financeiro cadastra a ordem direto no sistema, sem planilha. A ordem nasce
/// Nova e em carência, exatamente como a que vem da importação; o histórico diz
/// <c>cadastrada_manualmente</c> para separar do <c>importada</c>. Ao contrário dos comandos de ordem
/// existente, aqui a ordem ainda não existe — então este caso de uso não passa pelo
/// <see cref="ComandoDaOrdem.ExecutarAsync"/>, mas mantém o mesmo espírito:
/// <c>Idempotency-Key</c> para reenvio seguro, grava evento na mesma transação.
/// </summary>
internal static class CadastrarOrdemManual
{
    /// <summary>Equipamento de um cadastro manual. Série fica opcional porque pode ser preenchida só na retirada.</summary>
    internal sealed record EquipamentoCadastrado(TipoEquipamento Tipo, string Modelo, string? Serie, string? Mac, decimal? Valor);

    /// <summary>Endereço achatado para o formulário: a tela pede os campos soltos.</summary>
    internal sealed record EnderecoCadastrado(
        string Logradouro, string? Numero, string? Complemento, string Bairro, string Cidade, string Uf, string? Referencia);

    internal sealed record Pedido(
        string Cliente,
        int? CodigoClienteHubsoft,
        string? Observacao,
        EnderecoCadastrado Endereco,
        string? Cep,
        IReadOnlyList<string>? Telefones,
        IReadOnlyList<EquipamentoCadastrado> Equipamentos,
        string? TipoOs,
        int? CarenciaHoras);

    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapPost("/", TratarAsync)
            .RequireAuthorization(Politicas.Financeiro)
            .WithSummary("Cadastra uma ordem direto no sistema, sem planilha (RF03 estendido).")
            .WithDescription($"Exige o cabeçalho {ComandoDaOrdem.CabecalhoDeIdempotencia} com um UUID novo; reenviar a mesma chave devolve a ordem sem criar outra.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

    private static async Task<Results<Ok<OrdemDto>, ProblemHttpResult>> TratarAsync(
        Pedido pedido,
        [FromHeader(Name = ComandoDaOrdem.CabecalhoDeIdempotencia)] Guid? chaveDeIdempotencia,
        OrdensDbContext db,
        ClaimsPrincipal usuario,
        TimeProvider relogio,
        ParametrosDasOrdens parametros,
        CancellationToken ct)
    {
        if (chaveDeIdempotencia is not Guid chave || chave == Guid.Empty)
            return Problema(StatusCodes.Status400BadRequest, "IDEMPOTENCIA",
                $"Envie o cabeçalho {ComandoDaOrdem.CabecalhoDeIdempotencia} com um UUID novo para cada cadastro.");

        // Reenvio da mesma chave (caiu a conexão no cliente, mas a ordem já foi criada).
        var anterior = await db.Database
            .SqlQuery<Guid>($"SELECT ordem_id AS \"Value\" FROM retirada.evento_ordem WHERE chave_idempotencia = {chave}")
            .SingleOrDefaultAsync(ct);
        if (anterior != Guid.Empty)
        {
            var existente = await Projecoes.CarregarOrdemAsync(db, anterior, ct);
            if (existente is not null) return TypedResults.Ok(existente);
        }

        try
        {
            var agora = relogio.GetUtcNow();
            var endereco = new Endereco(
                pedido.Endereco.Logradouro.Trim(),
                string.IsNullOrWhiteSpace(pedido.Endereco.Numero) ? "s/n" : pedido.Endereco.Numero.Trim(),
                string.IsNullOrWhiteSpace(pedido.Endereco.Complemento) ? null : pedido.Endereco.Complemento.Trim(),
                string.IsNullOrWhiteSpace(pedido.Endereco.Bairro) ? "(não informado)" : pedido.Endereco.Bairro.Trim(),
                pedido.Endereco.Cidade.Trim(),
                (pedido.Endereco.Uf ?? string.Empty).Trim().ToUpperInvariant(),
                string.IsNullOrWhiteSpace(pedido.Endereco.Referencia) ? null : pedido.Endereco.Referencia.Trim());

            if (string.IsNullOrWhiteSpace(pedido.Cliente))
                throw new RegraDeNegocioException("RN02", "Informe o nome do cliente.");
            if (string.IsNullOrWhiteSpace(endereco.Logradouro))
                throw new RegraDeNegocioException("RN02", "Informe o endereço do cliente.");
            if (string.IsNullOrWhiteSpace(endereco.Cidade))
                throw new RegraDeNegocioException("RN02", "Informe a cidade do cliente.");
            if (pedido.Equipamentos.Count == 0)
                throw new RegraDeNegocioException("RN02", "Adicione pelo menos um equipamento para retirar.");

            var tipoOs = string.IsNullOrWhiteSpace(pedido.TipoOs) ? "RETIRADA DE EQUIPAMENTO" : pedido.TipoOs.Trim().ToUpperInvariant();
            var carencia = TimeSpan.FromHours(pedido.CarenciaHoras is int horas and > 0 ? horas : parametros.CarenciaHoras);

            // Cliente: pelo código do HubSoft (quando o financeiro souber); senão cria, igual o ingresso pela planilha.
            var clienteId = await ClienteAsync(db, pedido, ct);

            // Chave única para RN01: não deixa duas tentativas pela mesma tela (duplo clique do financeiro) virarem duas ordens.
            var chaveImportacao = "manual:" + Resumo($"{pedido.Cliente}|{endereco.Logradouro}|{endereco.Numero}|{endereco.Bairro}|{endereco.Cidade}|{endereco.Uf}|{agora:O}");

            var ordem = Ordem.CriarManual(
                chaveImportacao,
                tipoOs,
                clienteId,
                endereco,
                string.IsNullOrWhiteSpace(pedido.Cep) ? null : pedido.Cep.Trim(),
                string.IsNullOrWhiteSpace(pedido.Observacao) ? null : pedido.Observacao.Trim(),
                pedido.Equipamentos.Select(e => Equipamento.Criar(
                    e.Tipo, e.Modelo.Trim(),
                    string.IsNullOrWhiteSpace(e.Serie) ? null : NumeroSerie.Criar(e.Serie.Trim()),
                    string.IsNullOrWhiteSpace(e.Mac) ? null : e.Mac.Trim(),
                    e.Valor)),
                agora, carencia);

            await using var transacao = await db.Database.BeginTransactionAsync(ct);
            try
            {
                db.Ordens.Add(ordem);
                await db.SaveChangesAsync(ct);

                await GravarTelefonesAsync(db, clienteId, pedido.Telefones ?? [], parametros.ModoSimulacao, ct);

                var usuarioId = usuario.Id();
                var autor = new AutorDoEvento(usuarioId, usuario.Papel());
                await RegistroDeEventos.GravarAsync(
                    db, ordem.Id, ordem.Eventos[^1], null, ordem.Status, autor, chave, null, ct);

                await transacao.CommitAsync(ct);
            }
            catch (PostgresException e) when (
                e.SqlState == PostgresErrorCodes.UniqueViolation && e.ConstraintName == "evento_ordem_chave_idempotencia_key")
            {
                // Dois cliques ao mesmo tempo: o próximo request cai no caminho do reenvio acima.
                return Problema(StatusCodes.Status409Conflict, "CONFLITO",
                    "Essa chave já está sendo usada em outro cadastro. Atualize a tela e tente de novo.");
            }

            var dto = await Projecoes.CarregarOrdemAsync(db, ordem.Id, ct);
            return TypedResults.Ok(dto!);
        }
        catch (RegraDeNegocioException e)
        {
            return Problema(StatusCodes.Status422UnprocessableEntity, e.Codigo, e.Message);
        }
    }

    private static async Task<Guid> ClienteAsync(OrdensDbContext db, Pedido pedido, CancellationToken ct)
    {
        var conexao = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conexao.State != System.Data.ConnectionState.Open) await conexao.OpenAsync(ct);
        var tipoPessoa = System.Text.RegularExpressions.Regex.IsMatch(
            pedido.Cliente, @"\b(LTDA|EIRELI|EPP|MEI|ME|S\.?A\.?|S/A|CIA)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase) ? "PJ" : "PF";

        await using var cmd = new NpgsqlCommand("""
            INSERT INTO retirada.cliente (codigo_hubsoft, nome, tipo_pessoa) VALUES (@codigo, @nome, @tipo)
            ON CONFLICT (codigo_hubsoft) DO UPDATE SET nome = EXCLUDED.nome, atualizado_em = now()
            RETURNING id
            """, conexao);
        cmd.Parameters.AddWithValue("codigo", (object?)pedido.CodigoClienteHubsoft ?? DBNull.Value);
        cmd.Parameters.AddWithValue("nome", pedido.Cliente.Trim());
        cmd.Parameters.AddWithValue("tipo", tipoPessoa);
        return (Guid)(await cmd.ExecuteScalarAsync(ct))!;
    }

    private static async Task GravarTelefonesAsync(
        OrdensDbContext db, Guid clienteId, IReadOnlyList<string> telefones, bool modoSimulacao, CancellationToken ct)
    {
        foreach (var bruto in telefones.Where(t => !string.IsNullOrWhiteSpace(t)))
        {
            if (Interpretador.Telefone(bruto.Trim(), modoSimulacao) is not { } t) continue;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO retirada.telefone_cliente (cliente_id, numero_e164, como_veio, tipo, formato_valido)
                VALUES ({clienteId}, {t.NumeroE164}, {t.ComoVeio}, {t.Tipo}, {t.FormatoValido})
                ON CONFLICT (cliente_id, numero_e164) DO NOTHING
                """, ct);
        }
    }

    private static string Resumo(string texto)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(texto));
        return Convert.ToHexStringLower(bytes)[..16];
    }

    private static ProblemHttpResult Problema(int status, string codigo, string titulo) =>
        TypedResults.Problem(title: titulo, statusCode: status, extensions: new Dictionary<string, object?> { ["codigo"] = codigo });
}
