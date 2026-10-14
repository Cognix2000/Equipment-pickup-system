using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using Retirada.Ordens.Dominio;
using Retirada.Ordens.Infraestrutura;

namespace Retirada.Ordens.Importacao;

/// <summary>Resultado da confirmação, para o relatório (RF05).</summary>
internal sealed record ResultadoDaGravacao(int OrdensCriadas, int OrdensAtualizadas, int PendenciasRegistradas, int PendenciasResolvidas);

/// <summary>Lê do banco o que o arquivo cita e grava a importação confirmada.</summary>
internal static class Gravacao
{
    /// <summary>Uma consulta por tipo de dado, só com as chaves, protocolos e séries que estão no arquivo.</summary>
    public static async Task<RetratoDoBanco> CarregarRetratoAsync(OrdensDbContext db, IReadOnlyList<OrdemLida> lidas, CancellationToken ct)
    {
        var chaves = lidas.Select(l => l.Chave).Distinct().ToArray();
        var protocolos = lidas.Select(l => l.Referencias.Protocolo).OfType<string>().Distinct().ToArray();
        var idsOs = lidas.Select(l => l.Referencias.IdOrdemServico).OfType<int>().Distinct().ToArray();
        var series = lidas.Select(l => l.Equipamento?.Serie?.Valor).OfType<string>().Distinct().ToArray();

        var ordens = await db.Ordens.AsNoTracking().Include(o => o.Equipamentos)
            .Where(o => chaves.Contains(o.ChaveImportacao)).ToListAsync(ct);
        var porProtocolo = await db.Ordens.AsNoTracking()
            .Where(o => o.Protocolo != null && protocolos.Contains(o.Protocolo))
            .Select(o => new { o.Protocolo, o.ChaveImportacao }).ToListAsync(ct);
        var porIdOs = await db.Ordens.AsNoTracking()
            .Where(o => o.IdOrdemServicoHubsoft != null && idsOs.Contains(o.IdOrdemServicoHubsoft.Value))
            .Select(o => new { Id = o.IdOrdemServicoHubsoft!.Value, o.ChaveImportacao }).ToListAsync(ct);
        var seriesEmAberto = await db.Database.SqlQuery<SerieEmAberto>($"""
            SELECT e.numero_serie AS "Serie", o.chave_importacao AS "Chave"
            FROM retirada.equipamento_ordem e JOIN retirada.ordem o ON o.id = e.ordem_id
            WHERE e.numero_serie = ANY({series}) AND e.situacao IN ('pendente', 'retirado', 'divergente')
            """).ToListAsync(ct);
        var pendencias = await db.Database.SqlQuery<string>($"""
            SELECT chave_importacao AS "Value" FROM retirada.pendencia_importacao
            WHERE resolvida_em IS NULL AND chave_importacao = ANY({chaves})
            """).ToListAsync(ct);

        return new RetratoDoBanco(
            ordens.ToDictionary(o => o.ChaveImportacao),
            porProtocolo.ToDictionary(p => p.Protocolo!, p => p.ChaveImportacao),
            porIdOs.ToDictionary(p => p.Id, p => p.ChaveImportacao),
            seriesEmAberto.ToDictionary(s => s.Serie, s => s.Chave),
            pendencias.ToHashSet());
    }

    /// <summary>Registra a prévia: só o resumo, os erros por linha e o hash do arquivo. Nenhum dado pessoal fica guardado.</summary>
    public static async Task<Guid> RegistrarPreviaAsync(OrdensDbContext db, string nomeDoArquivo, byte[] conteudo, Guid usuarioId, Previa previa, CancellationToken ct)
    {
        var id = Guid.CreateVersion7();
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO retirada.importacao (id, arquivo_nome, arquivo_sha256, enviado_por, linhas_total, linhas_com_erro, colunas_descartadas, relatorio)
            VALUES (@id, @nome, @hash, @usuario, @total, @erros, @descartadas, @relatorio)
            """,
            [
                new NpgsqlParameter("id", id),
                new NpgsqlParameter("nome", Path.GetFileName(nomeDoArquivo)),
                new NpgsqlParameter("hash", Hash(conteudo)),
                new NpgsqlParameter("usuario", usuarioId),
                new NpgsqlParameter("total", previa.Linhas.Count),
                new NpgsqlParameter("erros", previa.Contar(ResultadoDaLinha.Erro)),
                new NpgsqlParameter("descartadas", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = previa.Colunas.Descartadas.ToArray() },
                new NpgsqlParameter("relatorio", NpgsqlDbType.Jsonb) { Value = Relatorio(previa, null) },
            ],
            ct);
        return id;
    }

    public static string Hash(byte[] conteudo) => Convert.ToHexStringLower(SHA256.HashData(conteudo));

    public static async Task<RegistroDeImportacao?> ObterAsync(OrdensDbContext db, Guid id, CancellationToken ct) =>
        await db.Database.SqlQuery<RegistroDeImportacao>($"""
            SELECT arquivo_sha256 AS "Hash", enviado_por AS "EnviadoPor", situacao AS "Situacao"
            FROM retirada.importacao WHERE id = {id}
            """).SingleOrDefaultAsync(ct);

    /// <summary>UC01: o financeiro confirma, e tudo é gravado de uma vez (uma transação).</summary>
    public static async Task<ResultadoDaGravacao> ConfirmarAsync(
        OrdensDbContext db, Guid importacaoId, Previa previa, AutorDoEvento autor, DateTimeOffset agora, TimeSpan carencia, CancellationToken ct)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(ct);
        var conexao = (NpgsqlConnection)db.Database.GetDbConnection();
        var tx = (NpgsqlTransaction)transacao.GetDbTransaction();

        // Trava a prévia logo no começo: duas confirmações ao mesmo tempo, só uma grava.
        var minha = await Comando(conexao, tx, "UPDATE retirada.importacao SET situacao = 'confirmada' WHERE id = @id AND situacao = 'previa'",
            ("id", importacaoId)).ExecuteNonQueryAsync(ct);
        if (minha == 0) throw new Retirada.Comum.RegraDeNegocioException("IMPORTACAO", "Essa importação já foi confirmada ou descartada.");

        // Clientes: pelo código do HubSoft quando vier; sem código, um cliente por nome dentro deste arquivo.
        var clientes = new Dictionary<string, Guid>();
        async Task<Guid> ClienteAsync(OrdemLida d)
        {
            var chave = d.CodigoCliente is { } c ? $"codigo:{c}" : $"nome:{d.Cliente.ToUpperInvariant()}";
            if (clientes.TryGetValue(chave, out var existente)) return existente;
            var id = (Guid)(await Comando(conexao, tx, """
                INSERT INTO retirada.cliente (codigo_hubsoft, nome, tipo_pessoa) VALUES (@codigo, @nome, @tipo)
                ON CONFLICT (codigo_hubsoft) DO UPDATE SET nome = EXCLUDED.nome, atualizado_em = now()
                RETURNING id
                """, ("codigo", (object?)d.CodigoCliente ?? DBNull.Value), ("nome", d.Cliente), ("tipo", d.TipoPessoa)).ExecuteScalarAsync(ct))!;
            clientes[chave] = id;
            return id;
        }

        async Task TelefonesAsync(Guid clienteId, IEnumerable<TelefoneLido> telefones)
        {
            foreach (var t in telefones)
                await Comando(conexao, tx, """
                    INSERT INTO retirada.telefone_cliente (cliente_id, numero_e164, como_veio, tipo, formato_valido)
                    VALUES (@cliente, @numero, @comoVeio, @tipo, @valido)
                    ON CONFLICT (cliente_id, numero_e164) DO NOTHING
                    """, ("cliente", clienteId), ("numero", t.NumeroE164), ("comoVeio", t.ComoVeio), ("tipo", t.Tipo), ("valido", t.FormatoValido))
                    .ExecuteNonQueryAsync(ct);
        }

        // Ordens novas, pelo próprio agregado: nascem como Nova, em carência (RN03).
        var novas = new List<(Ordem Ordem, GrupoDaOs Grupo)>();
        foreach (var grupo in previa.Novas)
        {
            var d = grupo.Dados;
            var clienteId = await ClienteAsync(d);
            await TelefonesAsync(clienteId, grupo.Telefones);
            var ordem = Ordem.Importar(
                d.Chave, d.NumeroOs, d.TipoOs, clienteId, d.Endereco, d.Localizacao, d.Observacao,
                grupo.Equipamentos.Select(e => Equipamento.Criar(e.Tipo, e.Modelo, e.Serie, e.Mac, e.Valor)),
                agora, carencia, d.Referencias with { ImportacaoId = importacaoId });
            db.Ordens.Add(ordem);
            novas.Add((ordem, grupo));
        }

        // Ordens que já existiam: só o que mudou (RN01).
        var chavesAtualizadas = previa.Atualizacoes.Select(g => g.Dados.Chave).ToArray();
        var existentes = await db.Ordens.Where(o => chavesAtualizadas.Contains(o.ChaveImportacao)).ToDictionaryAsync(o => o.ChaveImportacao, ct);
        var atualizadas = new List<(Ordem Ordem, StatusOrdem Status, IReadOnlyList<string> Mudancas)>();
        foreach (var grupo in previa.Atualizacoes)
        {
            if (!existentes.TryGetValue(grupo.Dados.Chave, out var ordem) || !ordem.AceitaAtualizacaoDaImportacao) continue;
            var mudancas = ordem.AtualizarPelaImportacao(grupo.Dados.ParaAtualizar(), agora);
            if (mudancas.Count > 0) atualizadas.Add((ordem, ordem.Status, mudancas));
            await TelefonesAsync(ordem.ClienteId, grupo.Telefones);
        }

        await db.SaveChangesAsync(ct);

        foreach (var (ordem, grupo) in novas)
            await RegistroDeEventos.GravarAsync(db, ordem.Id, ordem.Eventos[^1], null, ordem.Status, autor, Guid.NewGuid(),
                new { importacaoId, linhas = grupo.Linhas }, ct);
        foreach (var (ordem, status, mudancas) in atualizadas)
            await RegistroDeEventos.GravarAsync(db, ordem.Id, ordem.Eventos[^1], status, status, autor, Guid.NewGuid(),
                new { importacaoId, mudancas }, ct);

        // Pendência que agora virou ordem: a rescisão chegou.
        var resolvidas = 0;
        foreach (var (ordem, _) in novas)
            resolvidas += await Comando(conexao, tx, """
                UPDATE retirada.pendencia_importacao SET resolvida_em = now(), ordem_id = @ordem
                WHERE chave_importacao = @chave AND resolvida_em IS NULL
                """, ("ordem", ordem.Id), ("chave", ordem.ChaveImportacao)).ExecuteNonQueryAsync(ct);

        var registradas = 0;
        foreach (var p in previa.Pendencias)
            registradas += await Comando(conexao, tx, """
                INSERT INTO retirada.pendencia_importacao (importacao_id, chave_importacao, codigo_hubsoft, motivo)
                VALUES (@importacao, @chave, @codigo, @motivo)
                ON CONFLICT (chave_importacao) WHERE resolvida_em IS NULL DO NOTHING
                """, ("importacao", importacaoId), ("chave", p.Dados.Chave), ("codigo", (object?)p.Dados.CodigoCliente ?? DBNull.Value),
                ("motivo", p.Dados.Pendencia == MotivoDePendencia.BloqueioTemporario ? "bloqueio_temporario" : "sem_rescisao"))
                .ExecuteNonQueryAsync(ct);

        var resultado = new ResultadoDaGravacao(novas.Count, atualizadas.Count, registradas, resolvidas);
        await Comando(conexao, tx, """
            UPDATE retirada.importacao
            SET linhas_importadas = @importadas, linhas_com_erro = @erros, relatorio = @relatorio
            WHERE id = @id
            """,
            ("id", importacaoId),
            ("importadas", previa.Novas.Sum(g => g.Linhas.Count) + previa.Atualizacoes.Sum(g => g.Linhas.Count)),
            ("erros", previa.Contar(ResultadoDaLinha.Erro)),
            ("relatorio", new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = Relatorio(previa, resultado) }))
            .ExecuteNonQueryAsync(ct);

        await transacao.CommitAsync(ct);
        return resultado;
    }

    /// <summary>O que fica guardado da importação: contagens e erros por linha. Sem nome, endereço nem telefone.</summary>
    private static string Relatorio(Previa previa, ResultadoDaGravacao? gravacao) => JsonSerializer.Serialize(new
    {
        formato = previa.Formato.ToString().ToLowerInvariant(),
        linhas = Enum.GetValues<ResultadoDaLinha>().ToDictionary(r => r.ToString().ToLowerInvariant(), previa.Contar),
        erros = previa.Linhas.Where(l => l.Resultado == ResultadoDaLinha.Erro)
            .Select(l => new { linha = l.Numero, motivos = l.Detalhes.Where(d => !d.StartsWith("alerta:")) }),
        gravacao,
    });

    private static NpgsqlCommand Comando(NpgsqlConnection conexao, NpgsqlTransaction tx, string sql, params (string Nome, object Valor)[] parametros)
    {
        var comando = new NpgsqlCommand(sql, conexao, tx);
        foreach (var (nome, valor) in parametros)
        {
            if (valor is NpgsqlParameter pronto) { pronto.ParameterName = nome; comando.Parameters.Add(pronto); }
            else comando.Parameters.AddWithValue(nome, valor);
        }
        return comando;
    }

    internal sealed record SerieEmAberto(string Serie, string Chave);

    internal sealed record RegistroDeImportacao(string Hash, Guid EnviadoPor, string Situacao);
}
