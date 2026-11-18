using System.Globalization;
using System.Text;
using Retirada.Comum;

namespace Retirada.Ordens.Importacao;

/// <summary>Os campos que o sistema usa. Cada um aceita os nomes do HubSoft e os da planilha do financeiro.</summary>
internal enum Campo
{
    Cliente, TipoOs, Logradouro, Numero, Complemento, Referencia, Bairro, Cidade, Uf, Cep,
    Telefone1, Telefone2, Telefone3, Observacao,
    // DescricaoLivre: coluna de texto livre que pode conter telefones embutidos (típico do HubSoft em
    // `descricao_servico` e `descricao_abertura`). O conteúdo nunca é salvo em lugar nenhum — RN04 (LGPD)
    // descarta dado pessoal — e só é usado para extrair telefones via regex quando a coluna dedicada vem vazia.
    DescricaoLivre,
    DataProgramada, HoraProgramada, IdOrdemServico, Protocolo, NumeroOs, CodigoCliente, IdClienteServico,
    DataRescisao, SituacaoDoServico, Latitude, Longitude,
    EquipamentoTipo, EquipamentoModelo, NumeroSerie, Mac, ValorEquipamento,
}

/// <summary>Onde cada campo está no arquivo, e o que foi deixado de fora.</summary>
internal sealed record MapaDeColunas(IReadOnlyDictionary<Campo, int> Indices, IReadOnlyList<string> Reconhecidas, IReadOnlyList<string> Descartadas)
{
    public bool Tem(Campo campo) => Indices.ContainsKey(campo);

    public string? Valor(LinhaLida linha, Campo campo) =>
        Indices.TryGetValue(campo, out var i) && i < linha.Celulas.Count ? linha.Celulas[i] : null;
}

/// <summary>RF04: reconhece as colunas pelo nome. RN04: dado pessoal que a retirada não precisa nunca é lido.</summary>
internal static class Colunas
{
    private static readonly Dictionary<string, Campo> Sinonimos = new()
    {
        // Nomes do HubSoft (exportação real), da planilha do financeiro, e os que a gente ouve no dia a dia.
        // Tudo já normalizado (minúsculo, sem acento, sem pontuação).
        ["nome_razaosocial"] = Campo.Cliente, ["cliente"] = Campo.Cliente, ["nome"] = Campo.Cliente,
        ["nome_cliente"] = Campo.Cliente, ["nome_completo"] = Campo.Cliente, ["razao_social"] = Campo.Cliente,
        ["destinatario"] = Campo.Cliente, ["fulano"] = Campo.Cliente, ["assinante"] = Campo.Cliente,
        ["tipo_ordem_servico"] = Campo.TipoOs, ["tipo_de_os"] = Campo.TipoOs, ["tipo_os"] = Campo.TipoOs,
        ["tipo"] = Campo.TipoOs, ["servico"] = Campo.TipoOs,
        ["endereco"] = Campo.Logradouro, ["logradouro"] = Campo.Logradouro, ["rua"] = Campo.Logradouro,
        ["endereco_completo"] = Campo.Logradouro, ["local"] = Campo.Logradouro, ["via"] = Campo.Logradouro,
        ["avenida"] = Campo.Logradouro,
        ["numero"] = Campo.Numero, ["no"] = Campo.Numero, ["n"] = Campo.Numero, ["num"] = Campo.Numero,
        ["complemento"] = Campo.Complemento, ["compl"] = Campo.Complemento, ["apto"] = Campo.Complemento,
        ["apartamento"] = Campo.Complemento,
        ["referencia"] = Campo.Referencia, ["ponto_de_referencia"] = Campo.Referencia, ["ponto_referencia"] = Campo.Referencia,
        ["bairro"] = Campo.Bairro, ["vila"] = Campo.Bairro, ["localidade"] = Campo.Bairro, ["setor"] = Campo.Bairro,
        ["cidade"] = Campo.Cidade, ["municipio"] = Campo.Cidade,
        ["estado"] = Campo.Uf, ["uf"] = Campo.Uf,
        ["cep"] = Campo.Cep, ["codigo_postal"] = Campo.Cep,
        ["telefone_primario"] = Campo.Telefone1, ["telefone_1"] = Campo.Telefone1, ["telefone"] = Campo.Telefone1,
        ["celular"] = Campo.Telefone1, ["fone"] = Campo.Telefone1, ["contato"] = Campo.Telefone1,
        ["tel"] = Campo.Telefone1, ["tel_1"] = Campo.Telefone1, ["whatsapp"] = Campo.Telefone1, ["zap"] = Campo.Telefone1,
        ["telefone_secundario"] = Campo.Telefone2, ["telefone_2"] = Campo.Telefone2,
        ["celular_2"] = Campo.Telefone2, ["tel_2"] = Campo.Telefone2,
        ["telefone_terciario"] = Campo.Telefone3, ["telefone_3"] = Campo.Telefone3, ["tel_3"] = Campo.Telefone3,
        ["observacao"] = Campo.Observacao, ["observacoes"] = Campo.Observacao, ["obs"] = Campo.Observacao,
        ["anotacoes"] = Campo.Observacao, ["anotacao"] = Campo.Observacao,
        // Texto livre do HubSoft: usado só para extrair telefones (LGPD RN04: nada do conteúdo é guardado).
        ["descricao_servico"] = Campo.DescricaoLivre, ["descricao_abertura"] = Campo.DescricaoLivre,
        ["descricao_fechamento"] = Campo.DescricaoLivre,
        ["data_inicio_programado"] = Campo.DataProgramada, ["data_programada"] = Campo.DataProgramada, ["data"] = Campo.DataProgramada,
        ["hora_inicio_programado"] = Campo.HoraProgramada, ["hora_programada"] = Campo.HoraProgramada,
        ["id_ordem_servico"] = Campo.IdOrdemServico,
        ["protocolo"] = Campo.Protocolo,
        ["numero_ordem_servico"] = Campo.NumeroOs, ["no_os"] = Campo.NumeroOs, ["numero_os"] = Campo.NumeroOs,
        ["codigo_cliente"] = Campo.CodigoCliente, ["codigo_do_cliente"] = Campo.CodigoCliente,
        ["id_cliente_servico"] = Campo.IdClienteServico, ["id_servico_do_cliente"] = Campo.IdClienteServico,
        ["data_rescisao"] = Campo.DataRescisao, ["data_da_rescisao"] = Campo.DataRescisao,
        ["status_do_servico"] = Campo.SituacaoDoServico, ["situacao_do_servico"] = Campo.SituacaoDoServico,
        ["latitude"] = Campo.Latitude, ["longitude"] = Campo.Longitude,
        ["equipamento_tipo"] = Campo.EquipamentoTipo, ["tipo_equipamento"] = Campo.EquipamentoTipo,
        ["equipamento_modelo"] = Campo.EquipamentoModelo, ["modelo"] = Campo.EquipamentoModelo,
        ["numero_serie"] = Campo.NumeroSerie, ["no_de_serie"] = Campo.NumeroSerie, ["serie"] = Campo.NumeroSerie,
        ["mac_address"] = Campo.Mac, ["mac"] = Campo.Mac,
        ["valor_equipamento"] = Campo.ValorEquipamento,
    };

    /// <summary>
    /// Só o essencial para a ordem servir para alguma coisa: o cliente, onde buscar e em qual cidade.
    /// O resto (tipo da OS, telefone, bairro, UF, número) o sistema preenche com um padrão e avisa na prévia (RF04).
    /// </summary>
    private static readonly Dictionary<Campo, string> NomeNaPlanilha = new()
    {
        [Campo.Cliente] = "cliente (nome/nome_razaosocial/destinatario)",
        [Campo.Logradouro] = "endereço (endereco/logradouro/rua)",
        [Campo.Cidade] = "cidade",
    };

    public static MapaDeColunas Mapear(IReadOnlyList<string> cabecalhos)
    {
        var indices = new Dictionary<Campo, int>();
        var reconhecidas = new List<string>();
        var descartadas = new List<string>();

        for (var i = 0; i < cabecalhos.Count; i++)
        {
            var nome = Normalizar(cabecalhos[i]);
            // A planilha do HubSoft traz a observação numa coluna sem nome (a 15ª).
            if (nome.Length == 0 && !indices.ContainsKey(Campo.Observacao))
            {
                indices[Campo.Observacao] = i;
                reconhecidas.Add("observação (coluna sem nome)");
            }
            else if (Sinonimos.TryGetValue(nome, out var campo) && !indices.ContainsKey(campo))
            {
                indices[campo] = i;
                reconhecidas.Add(cabecalhos[i]);
            }
            else if (nome.Length > 0)
            {
                descartadas.Add(cabecalhos[i]); // RN04: CPF/CNPJ, e-mail, plano, PPPoE e qualquer coluna sem uso
            }
        }

        var faltando = NomeNaPlanilha.Keys.Where(c => !indices.ContainsKey(c)).Select(c => NomeNaPlanilha[c]).ToList();
        if (faltando.Count > 0)
            throw new RegraDeNegocioException("RF04", faltando.Count == 1
                ? $"Falta a coluna {faltando[0]}. Confira o cabeçalho e envie de novo."
                : $"Faltam as colunas {string.Join(", ", faltando)}. Confira o cabeçalho e envie de novo.");

        return new MapaDeColunas(indices, reconhecidas, descartadas);
    }

    /// <summary>"Nº de série" → "no_de_serie"; "Código do cliente" → "codigo_do_cliente".</summary>
    internal static string Normalizar(string cabecalho)
    {
        var semAcento = new StringBuilder();
        foreach (var ch in cabecalho.Replace('º', 'o').Replace('ª', 'a').Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) semAcento.Append(ch);

        var resultado = new StringBuilder();
        foreach (var ch in semAcento.ToString().ToLowerInvariant())
            resultado.Append(char.IsAsciiLetterOrDigit(ch) ? ch : '_');
        var texto = resultado.ToString();
        while (texto.Contains("__")) texto = texto.Replace("__", "_");
        return texto.Trim('_');
    }
}
