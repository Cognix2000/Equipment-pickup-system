using Retirada.Ordens.Dominio;

namespace Retirada.Ordens.Importacao;

internal enum ResultadoDaLinha { Nova, Atualizacao, Repetida, Duplicada, Pendencia, Erro, Ignorada }

/// <summary>O que já está gravado e importa para classificar o arquivo. Carregado de uma vez antes da prévia.</summary>
internal sealed record RetratoDoBanco(
    IReadOnlyDictionary<string, Ordem> OrdensPorChave,
    IReadOnlyDictionary<string, string> ChavePorProtocolo,
    IReadOnlyDictionary<int, string> ChavePorIdOs,
    IReadOnlyDictionary<string, string> ChavePorSerieEmAberto,
    IReadOnlySet<string> PendenciasAbertas)
{
    public static readonly RetratoDoBanco Vazio = new(
        new Dictionary<string, Ordem>(), new Dictionary<string, string>(), new Dictionary<int, string>(),
        new Dictionary<string, string>(), new HashSet<string>());
}

internal sealed record LinhaDaPrevia(
    int Numero, ResultadoDaLinha Resultado, string? Cliente, string? Endereco, string? Bairro, string? TipoOs,
    IReadOnlyList<string> Detalhes);

/// <summary>Uma OS do arquivo: os dados da primeira linha e os equipamentos de todas as linhas dela.</summary>
internal sealed class GrupoDaOs(OrdemLida dados, int primeiraLinha)
{
    public OrdemLida Dados { get; } = dados;
    public int PrimeiraLinha { get; } = primeiraLinha;
    public List<int> Linhas { get; } = [primeiraLinha];
    public List<EquipamentoLido> Equipamentos { get; } = dados.Equipamento is { } e ? [e] : [];
    public List<TelefoneLido> Telefones { get; } = [.. dados.Telefones];
    public Ordem? Existente { get; set; }
    public IReadOnlyList<string> Mudancas { get; set; } = [];
    public string? Erro { get; set; }
}

internal sealed record PendenciaDaPrevia(OrdemLida Dados, int Linha);

/// <summary>RF03 e RF05: o resultado de cada linha, e o que a confirmação vai gravar.</summary>
internal sealed record Previa(
    FormatoDoArquivo Formato,
    MapaDeColunas Colunas,
    IReadOnlyList<LinhaDaPrevia> Linhas,
    IReadOnlyList<GrupoDaOs> Novas,
    IReadOnlyList<GrupoDaOs> Atualizacoes,
    IReadOnlyList<PendenciaDaPrevia> Pendencias,
    IReadOnlyList<string> AvisosDoArquivo)
{
    public int Contar(ResultadoDaLinha r) => Linhas.Count(l => l.Resultado == r);
}

internal static class MontadorDaPrevia
{
    /// <summary>Lê, interpreta e classifica. Não grava nada.</summary>
    public static Previa Montar(ArquivoLido arquivo, MapaDeColunas colunas, IReadOnlyList<LinhaInterpretada> linhas, RetratoDoBanco banco)
    {
        var resultado = new Dictionary<int, (ResultadoDaLinha Resultado, List<string> Detalhes)>();
        var grupos = new Dictionary<string, GrupoDaOs>();
        var pendencias = new Dictionary<string, PendenciaDaPrevia>();
        var seriesNoArquivo = new Dictionary<string, string>(); // série → chave da OS que a trouxe primeiro

        foreach (var linha in linhas)
        {
            var detalhes = new List<string>(linha.Alertas.Select(a => "alerta: " + a));
            if (linha.FoiIgnorada)
            {
                // Pulada silenciosamente — típico do HubSoft que exporta tipos misturados. Não é erro.
                resultado[linha.Numero] = (ResultadoDaLinha.Ignorada, [linha.MotivoIgnorada!]);
                continue;
            }
            if (linha.Ordem is not { } dados)
            {
                detalhes.InsertRange(0, linha.Erros);
                resultado[linha.Numero] = (ResultadoDaLinha.Erro, detalhes);
                continue;
            }

            if (dados.Pendencia is { } motivo)
            {
                if (pendencias.TryGetValue(dados.Chave, out var anterior))
                    resultado[linha.Numero] = (ResultadoDaLinha.Duplicada, [$"repete a linha {anterior.Linha}", .. detalhes]);
                else if (banco.PendenciasAbertas.Contains(dados.Chave))
                    resultado[linha.Numero] = (ResultadoDaLinha.Repetida, ["pendência já registrada", .. detalhes]);
                else if (banco.OrdensPorChave.ContainsKey(dados.Chave))
                    resultado[linha.Numero] = (ResultadoDaLinha.Repetida, ["a OS já virou ordem; a linha nova é ignorada", .. detalhes]);
                else
                {
                    pendencias[dados.Chave] = new PendenciaDaPrevia(dados, linha.Numero);
                    var texto = motivo == MotivoDePendencia.BloqueioTemporario ? "bloqueio temporário" : "serviço ainda sem rescisão";
                    resultado[linha.Numero] = (ResultadoDaLinha.Pendencia, [texto, .. detalhes]);
                }
                continue;
            }

            if (grupos.TryGetValue(dados.Chave, out var grupo))
            {
                // Mais uma linha da mesma OS: é mais um equipamento, ou uma repetição.
                var equipamentoNovo = dados.Equipamento is { } e && !grupo.Equipamentos.Any(x => x.Serie == e.Serie && x.Tipo == e.Tipo && x.Modelo == e.Modelo);
                if (!Equals(dados.DadosDaOs, grupo.Dados.DadosDaOs))
                    resultado[linha.Numero] = (ResultadoDaLinha.Duplicada, [$"mesma OS da linha {grupo.PrimeiraLinha}, com dados diferentes: vale a linha {grupo.PrimeiraLinha}", .. detalhes]);
                else if (equipamentoNovo)
                {
                    grupo.Equipamentos.Add(dados.Equipamento!);
                    grupo.Linhas.Add(linha.Numero);
                    resultado[linha.Numero] = (ResultadoDaLinha.Nova, [$"mais um equipamento da OS da linha {grupo.PrimeiraLinha}", .. detalhes]);
                }
                else
                    resultado[linha.Numero] = (ResultadoDaLinha.Duplicada, [$"repete a linha {grupo.PrimeiraLinha}", .. detalhes]);
                continue;
            }

            grupo = new GrupoDaOs(dados, linha.Numero);
            grupos[dados.Chave] = grupo;
            resultado[linha.Numero] = (ResultadoDaLinha.Nova, detalhes);
        }

        // Cada OS contra o banco e contra as outras OS do arquivo.
        foreach (var grupo in grupos.Values)
        {
            var chave = grupo.Dados.Chave;
            var referencias = grupo.Dados.Referencias;
            if (referencias.Protocolo is { } protocolo && banco.ChavePorProtocolo.TryGetValue(protocolo, out var outraP) && outraP != chave)
                grupo.Erro = "o protocolo já está em outra ordem";
            else if (referencias.IdOrdemServico is { } idOs && banco.ChavePorIdOs.TryGetValue(idOs, out var outraO) && outraO != chave)
                grupo.Erro = "o id da OS já está em outra ordem";

            banco.OrdensPorChave.TryGetValue(chave, out var existente);
            grupo.Existente = existente;
            if (existente is null && grupo.Erro is null)
            {
                // RN11: a série só pode estar em aberto em uma ordem por vez.
                foreach (var serie in grupo.Equipamentos.Where(e => e.Serie is not null).Select(e => e.Serie!.Valor))
                {
                    if ((banco.ChavePorSerieEmAberto.TryGetValue(serie, out var dona) && dona != chave)
                        || (seriesNoArquivo.TryGetValue(serie, out var noArquivo) && noArquivo != chave))
                    {
                        grupo.Erro = $"a série {serie} já está em aberto em outra ordem (RN11)";
                        break;
                    }
                    seriesNoArquivo[serie] = chave;
                }
            }

            if (grupo.Erro is not null)
            {
                foreach (var numero in grupo.Linhas)
                    resultado[numero] = (ResultadoDaLinha.Erro, [grupo.Erro, .. resultado[numero].Detalhes]);
                continue;
            }

            if (existente is null)
            {
                if (banco.PendenciasAbertas.Contains(chave))
                    resultado[grupo.PrimeiraLinha].Detalhes.Insert(0, "resolve a pendência: a rescisão chegou");
                continue;
            }

            // RN01: reimportar não duplica. Só atualiza o que mudou, e só em ordem que não terminou.
            ResultadoDaLinha estado;
            string detalhe;
            if (!existente.AceitaAtualizacaoDaImportacao)
                (estado, detalhe) = (ResultadoDaLinha.Repetida, "ordem já encerrada: fica como está");
            else
            {
                grupo.Mudancas = existente.MudancasDaImportacao(grupo.Dados.ParaAtualizar());
                (estado, detalhe) = grupo.Mudancas.Count > 0
                    ? (ResultadoDaLinha.Atualizacao, "mudou: " + string.Join(", ", grupo.Mudancas))
                    : (ResultadoDaLinha.Repetida, "já importada, sem mudança");
            }
            foreach (var numero in grupo.Linhas)
                resultado[numero] = (estado, [detalhe, .. resultado[numero].Detalhes.Where(d => !d.StartsWith("mais um equipamento"))]);
        }

        var dadosPorLinha = linhas.ToDictionary(l => l.Numero, l => l.Ordem);
        var linhasDaPrevia = linhas.Select(l =>
        {
            var (estado, detalhes) = resultado[l.Numero];
            var d = dadosPorLinha[l.Numero];
            return new LinhaDaPrevia(l.Numero, estado, d?.Cliente, d is null ? null : EnderecoCurto(d.Endereco), d?.Endereco.Bairro, d?.TipoOs, detalhes);
        }).ToList();

        var avisos = new List<string>();
        if (!colunas.Tem(Campo.IdOrdemServico) && !colunas.Tem(Campo.Protocolo))
            avisos.Add("Sem id_ordem_servico nem protocolo no arquivo: a chave é provisória. Enviar a mesma linha de novo não duplica.");
        if (!colunas.Tem(Campo.TipoOs))
            avisos.Add("Sem coluna de tipo de OS: todas entraram como \"RETIRADA DE EQUIPAMENTO\".");
        if (!colunas.Tem(Campo.Bairro))
            avisos.Add("Sem coluna de bairro: as ordens entram sem bairro (aparece como \"(não informado)\").");
        if (!colunas.Tem(Campo.Uf))
            avisos.Add("Sem coluna de UF: o estado fica vazio nas ordens.");
        if (!colunas.Tem(Campo.Numero))
            avisos.Add("Sem coluna de número: todas entram como \"s/n\".");
        if (!colunas.Tem(Campo.Telefone1) && !colunas.Tem(Campo.Telefone2) && !colunas.Tem(Campo.Telefone3))
            avisos.Add("Sem colunas de telefone: o contato fica só pela visita ao endereço.");
        if (!colunas.Tem(Campo.Latitude) || !colunas.Tem(Campo.Longitude))
            avisos.Add("O arquivo não traz latitude e longitude: as ordens novas aparecem só na lista, até o técnico passar o GPS em campo.");
        if (!colunas.Tem(Campo.EquipamentoTipo))
            avisos.Add("O arquivo não traz os equipamentos: o técnico registra tipo, modelo e série em campo.");

        var validos = grupos.Values.Where(g => g.Erro is null).ToList();
        return new Previa(
            arquivo.Formato, colunas, linhasDaPrevia,
            validos.Where(g => g.Existente is null).ToList(),
            validos.Where(g => g.Mudancas.Count > 0).ToList(),
            [.. pendencias.Values],
            avisos);
    }

    private static string EnderecoCurto(Endereco e) => e.Numero is null ? e.Logradouro : $"{e.Logradouro}, {e.Numero}";
}
