using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using ExcelDataReader;
using Retirada.Comum;

namespace Retirada.Ordens.Importacao;

internal enum FormatoDoArquivo { Xlsx, Xls, Ods, Csv, Json, Xml }

/// <summary>Uma linha do arquivo, com o número que o financeiro vê na planilha (o cabeçalho é a linha 1).</summary>
internal sealed record LinhaLida(int Numero, IReadOnlyList<string?> Celulas);

/// <summary>RN15: qualquer formato aceito vira esta mesma estrutura antes da validação.</summary>
internal sealed record ArquivoLido(FormatoDoArquivo Formato, IReadOnlyList<string> Cabecalhos, IReadOnlyList<LinhaLida> Linhas);

/// <summary>
/// Lê o arquivo e reconhece o formato pelo conteúdo, não pela extensão (UC01).
/// RN16: nada do arquivo é executado. Planilhas entregam o valor já calculado de cada célula;
/// macros, fórmulas, DTDs e entidades externas são ignorados.
/// </summary>
internal static class LeitorDeArquivo
{
    public const long LimiteDeBytes = 10 * 1024 * 1024;  // RN15
    private const long LimiteDescompactado = 100 * 1024 * 1024;
    private const int LimiteDeLinhas = 50_000;
    private const int LimiteDeColunas = 200;

    private static readonly string[] ExtensoesRecusadas = [".sql", ".pdf", ".bak", ".dump"];

    static LeitorDeArquivo() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // .xls e CSV do Windows

    public static ArquivoLido Ler(byte[] conteudo, string nomeDoArquivo)
    {
        var extensao = Path.GetExtension(nomeDoArquivo).ToLowerInvariant();
        if (extensao == ".sql")
            throw Recusa("Arquivo .sql não é aceito: exporte a consulta como .csv ou .json.");
        if (ExtensoesRecusadas.Contains(extensao) || Comeca(conteudo, "%PDF"u8))
            throw Recusa("Formato não aceito. Use .xlsx, .xls, .ods, .csv, .txt, .json ou .xml.");
        if (conteudo.Length == 0) throw Recusa("O arquivo está vazio.");
        if (conteudo.Length > LimiteDeBytes) throw Recusa("O arquivo passa de 10 MB. Divida a planilha em partes menores.");

        if (Comeca(conteudo, [0x50, 0x4B, 0x03, 0x04])) return LerZip(conteudo);
        if (Comeca(conteudo, [0xD0, 0xCF, 0x11, 0xE0])) return LerPlanilhaDoExcel(conteudo, FormatoDoArquivo.Xls);

        var texto = Decodificar(conteudo);
        var inicio = texto.TrimStart('﻿', ' ', '\t', '\r', '\n');
        if (inicio.StartsWith('[') || inicio.StartsWith('{')) return LerJson(texto);
        if (inicio.StartsWith('<')) return LerXml(texto);
        return LerTextoSeparado(texto);
    }

    // ------------------------------------------------------------------ planilhas (zip)

    private static ArquivoLido LerZip(byte[] conteudo)
    {
        using var zip = new ZipArchive(new MemoryStream(conteudo), ZipArchiveMode.Read);
        // Bomba de descompactação: confere o tamanho declarado antes de abrir qualquer parte.
        if (zip.Entries.Sum(e => e.Length) > LimiteDescompactado)
            throw Recusa("O arquivo descompactado é grande demais para uma planilha de retiradas.");

        var tipo = zip.GetEntry("mimetype") is { } m ? new StreamReader(m.Open()).ReadToEnd().Trim() : null;
        if (tipo == "application/vnd.oasis.opendocument.spreadsheet") return LerOds(zip);
        if (zip.GetEntry("xl/workbook.xml") is not null) return LerPlanilhaDoExcel(conteudo, FormatoDoArquivo.Xlsx);
        throw Recusa("O arquivo compactado não é uma planilha (.xlsx ou .ods).");
    }

    /// <summary>.xlsx e .xls: usa a primeira aba que tiver dados.</summary>
    private static ArquivoLido LerPlanilhaDoExcel(byte[] conteudo, FormatoDoArquivo formato)
    {
        using var leitor = ExcelReaderFactory.CreateReader(new MemoryStream(conteudo));
        do
        {
            var linhas = new List<string?[]>();
            while (leitor.Read())
            {
                var celulas = new string?[Math.Min(leitor.FieldCount, LimiteDeColunas)];
                for (var i = 0; i < celulas.Length; i++) celulas[i] = Texto(leitor.GetValue(i));
                linhas.Add(celulas);
                if (linhas.Count > LimiteDeLinhas + 1) throw MuitasLinhas();
            }
            if (linhas.Any(l => l.Any(c => c is not null))) return Montar(formato, linhas);
        } while (leitor.NextResult());

        throw Recusa("A planilha não tem nenhuma linha preenchida.");
    }

    /// <summary>.ods: lê o content.xml da primeira tabela com dados.</summary>
    private static ArquivoLido LerOds(ZipArchive zip)
    {
        const string Tabela = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
        const string Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
        const string TextoNs = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";

        var conteudo = zip.GetEntry("content.xml") ?? throw Recusa("Planilha .ods sem conteúdo.");
        using var xml = XmlReader.Create(conteudo.Open(), ConfiguracaoXmlSegura());

        List<string?[]>? atual = null;
        List<string?>? linha = null;
        var repeticaoDaLinha = 1;
        while (xml.Read())
        {
            if (xml.NodeType == XmlNodeType.Element && xml.NamespaceURI == Tabela)
            {
                switch (xml.LocalName)
                {
                    case "table":
                        atual = [];
                        break;
                    case "table-row":
                        linha = [];
                        repeticaoDaLinha = int.TryParse(xml.GetAttribute("number-rows-repeated", Tabela), out var r) ? r : 1;
                        if (xml.IsEmptyElement) linha = null;
                        break;
                    case "table-cell" or "covered-table-cell" when linha is not null:
                        var repeticao = int.TryParse(xml.GetAttribute("number-columns-repeated", Tabela), out var c) ? c : 1;
                        var valor = ValorDaCelulaOds(xml, Office, TextoNs);
                        for (var i = 0; i < repeticao && linha.Count < LimiteDeColunas; i++) linha.Add(valor);
                        break;
                }
            }
            else if (xml.NodeType == XmlNodeType.EndElement && xml.NamespaceURI == Tabela)
            {
                if (xml.LocalName == "table-row" && linha is not null && atual is not null)
                {
                    // Linhas vazias repetidas (o fim da aba) não viram linhas de verdade.
                    if (linha.Any(v => v is not null))
                        for (var i = 0; i < repeticaoDaLinha; i++) atual.Add([.. linha]);
                    if (atual.Count > LimiteDeLinhas + 1) throw MuitasLinhas();
                    linha = null;
                }
                else if (xml.LocalName == "table" && atual is { Count: > 0 })
                {
                    return Montar(FormatoDoArquivo.Ods, atual);
                }
            }
        }
        throw Recusa("A planilha não tem nenhuma linha preenchida.");
    }

    private static string? ValorDaCelulaOds(XmlReader xml, string office, string textoNs)
    {
        var tipo = xml.GetAttribute("value-type", office);
        var valor = tipo switch
        {
            "float" or "percentage" or "currency" => xml.GetAttribute("value", office),
            "date" => xml.GetAttribute("date-value", office) is { } d && DateTime.TryParse(d, CultureInfo.InvariantCulture, DateTimeStyles.None, out var data)
                ? Texto(data) : null,
            "time" => xml.GetAttribute("time-value", office),
            "boolean" => xml.GetAttribute("boolean-value", office),
            _ => null,
        };
        if (xml.IsEmptyElement) return Limpo(valor);

        // O texto da célula fica nos parágrafos <text:p>; lê até fechar a célula.
        var profundidade = xml.Depth;
        var texto = new StringBuilder();
        while (xml.Read() && !(xml.NodeType == XmlNodeType.EndElement && xml.Depth == profundidade))
        {
            if (xml.NodeType is XmlNodeType.Text or XmlNodeType.SignificantWhitespace) texto.Append(xml.Value);
            else if (xml.NodeType == XmlNodeType.Element && xml.NamespaceURI == textoNs && xml.LocalName == "p" && texto.Length > 0) texto.Append('\n');
            else if (xml.NodeType == XmlNodeType.Element && xml.NamespaceURI == textoNs && xml.LocalName == "s") texto.Append(' ');
        }
        return Limpo(valor ?? texto.ToString());
    }

    // ------------------------------------------------------------------ texto

    /// <summary>UTF-8 quando é UTF-8 válido; senão a codificação do Windows (o bloco de notas antigo grava assim).</summary>
    private static string Decodificar(byte[] conteudo)
    {
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(conteudo).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1252).GetString(conteudo);
        }
    }

    /// <summary>.csv e .txt: descobre o separador (vírgula, ponto e vírgula, tabulação ou barra) pelo cabeçalho.</summary>
    private static ArquivoLido LerTextoSeparado(string texto)
    {
        var primeiraLinha = texto.Split('\n', 2)[0];
        char[] candidatos = [';', ',', '\t', '|'];
        var separador = candidatos.MaxBy(c => ContarForaDeAspas(primeiraLinha, c));
        if (ContarForaDeAspas(primeiraLinha, separador) == 0)
            throw Recusa("Não achei o separador das colunas. Use vírgula, ponto e vírgula, tabulação ou barra.");

        var linhas = new List<string?[]>();
        var campos = new List<string?>();
        var campo = new StringBuilder();
        var entreAspas = false;
        for (var i = 0; i < texto.Length; i++)
        {
            var ch = texto[i];
            if (entreAspas)
            {
                if (ch == '"' && i + 1 < texto.Length && texto[i + 1] == '"') { campo.Append('"'); i++; }
                else if (ch == '"') entreAspas = false;
                else campo.Append(ch);
            }
            else if (ch == '"' && campo.Length == 0) entreAspas = true;
            else if (ch == separador) { campos.Add(Limpo(campo.ToString())); campo.Clear(); }
            else if (ch is '\n' or '\r')
            {
                if (ch == '\r' && i + 1 < texto.Length && texto[i + 1] == '\n') i++;
                campos.Add(Limpo(campo.ToString())); campo.Clear();
                linhas.Add([.. campos.Take(LimiteDeColunas)]); campos.Clear();
                if (linhas.Count > LimiteDeLinhas + 1) throw MuitasLinhas();
            }
            else campo.Append(ch);
        }
        if (campo.Length > 0 || campos.Count > 0)
        {
            campos.Add(Limpo(campo.ToString()));
            linhas.Add([.. campos.Take(LimiteDeColunas)]);
        }
        return Montar(FormatoDoArquivo.Csv, linhas);
    }

    private static int ContarForaDeAspas(string linha, char separador)
    {
        var total = 0;
        var entreAspas = false;
        foreach (var ch in linha)
        {
            if (ch == '"') entreAspas = !entreAspas;
            else if (ch == separador && !entreAspas) total++;
        }
        return total;
    }

    /// <summary>.json: lista de registros, ou um objeto com uma lista (ex.: { "ordens": [...] }).</summary>
    private static ArquivoLido LerJson(string texto)
    {
        JsonDocument documento;
        try
        {
            documento = JsonDocument.Parse(texto, new JsonDocumentOptions { MaxDepth = 16, AllowTrailingCommas = true });
        }
        catch (JsonException)
        {
            throw Recusa("O arquivo .json não está bem formado.");
        }

        using (documento)
        {
            var lista = documento.RootElement.ValueKind == JsonValueKind.Array
                ? documento.RootElement
                : documento.RootElement.EnumerateObject().Select(p => p.Value).FirstOrDefault(v => v.ValueKind == JsonValueKind.Array);
            if (lista.ValueKind != JsonValueKind.Array)
                throw Recusa("O .json precisa ser uma lista de registros, com os mesmos campos da planilha.");

            var registros = lista.EnumerateArray()
                .Where(r => r.ValueKind == JsonValueKind.Object)
                .Select(r => r.EnumerateObject().Select(p => (p.Name, Valor: TextoJson(p.Value))).ToList())
                .ToList();
            return DeRegistros(FormatoDoArquivo.Json, registros);
        }
    }

    private static string? TextoJson(JsonElement valor) => valor.ValueKind switch
    {
        JsonValueKind.String => Limpo(valor.GetString()),
        JsonValueKind.Number => valor.GetRawText(),
        JsonValueKind.True => "sim",
        JsonValueKind.False => "não",
        _ => null, // nulo, lista ou objeto aninhado: não é campo da planilha
    };

    /// <summary>.xml: um elemento por registro, com um filho (ou atributo) por campo.</summary>
    private static ArquivoLido LerXml(string texto)
    {
        XElement raiz;
        try
        {
            // O leitor seguro barra DTD e entidades externas antes de montar a árvore.
            using var xml = XmlReader.Create(new StringReader(texto), ConfiguracaoXmlSegura());
            raiz = XElement.Load(xml);
        }
        catch (XmlException)
        {
            throw Recusa("O arquivo .xml não está bem formado ou usa DTD, que não é aceito.");
        }

        var registros = raiz.Elements()
            .Take(LimiteDeLinhas + 1)
            .Select(r => r.Attributes().Select(a => (a.Name.LocalName, Limpo(a.Value)))
                .Concat(r.Elements().Select(c => (c.Name.LocalName, Limpo(c.Value))))
                .ToList())
            .ToList();
        if (registros.Count > LimiteDeLinhas) throw MuitasLinhas();
        return DeRegistros(FormatoDoArquivo.Xml, registros);
    }

    /// <summary>JSON e XML: as colunas são a união dos campos, na ordem em que aparecem.</summary>
    private static ArquivoLido DeRegistros(FormatoDoArquivo formato, List<List<(string Nome, string? Valor)>> registros)
    {
        var cabecalhos = new List<string>();
        foreach (var (nome, _) in registros.SelectMany(r => r))
            if (!cabecalhos.Contains(nome) && cabecalhos.Count < LimiteDeColunas) cabecalhos.Add(nome);

        var linhas = registros.Select((r, i) =>
        {
            var celulas = new string?[cabecalhos.Count];
            foreach (var (nome, valor) in r)
            {
                var indice = cabecalhos.IndexOf(nome);
                if (indice >= 0) celulas[indice] = valor;
            }
            return new LinhaLida(i + 2, celulas); // registro 1 = "linha 2", como numa planilha com cabeçalho
        }).ToList();
        if (linhas.Count == 0) throw Recusa("O arquivo não tem nenhum registro.");
        return new ArquivoLido(formato, cabecalhos, linhas);
    }

    // ------------------------------------------------------------------ comum

    /// <summary>A primeira linha preenchida é o cabeçalho; linhas vazias no meio são puladas.</summary>
    private static ArquivoLido Montar(FormatoDoArquivo formato, List<string?[]> linhas)
    {
        var indiceDoCabecalho = linhas.FindIndex(l => l.Any(c => c is not null));
        if (indiceDoCabecalho < 0) throw Recusa("O arquivo não tem nenhuma linha preenchida.");

        // Largura = última coluna com algum valor, no cabeçalho ou nos dados. A planilha do HubSoft
        // traz a observação numa coluna sem nome no fim, e ela não pode sumir.
        var largura = linhas.Skip(indiceDoCabecalho).Max(l => Array.FindLastIndex(l, c => c is not null) + 1);
        var cabecalhos = Enumerable.Range(0, largura)
            .Select(i => i < linhas[indiceDoCabecalho].Length ? linhas[indiceDoCabecalho][i] ?? string.Empty : string.Empty)
            .ToList();

        var dados = new List<LinhaLida>();
        for (var i = indiceDoCabecalho + 1; i < linhas.Count; i++)
        {
            if (!linhas[i].Any(c => c is not null)) continue;
            var celulas = new string?[cabecalhos.Count];
            Array.Copy(linhas[i], celulas, Math.Min(linhas[i].Length, celulas.Length));
            dados.Add(new LinhaLida(i + 1, celulas));
        }
        if (dados.Count == 0) throw Recusa("O arquivo só tem o cabeçalho, sem nenhuma ordem.");
        if (dados.Count > LimiteDeLinhas) throw MuitasLinhas();
        return new ArquivoLido(formato, cabecalhos, dados);
    }

    /// <summary>Valor de célula como texto. Data com hora zero sai só como data (dd/MM/aaaa).</summary>
    internal static string? Texto(object? valor) => valor switch
    {
        null or DBNull => null,
        string s => Limpo(s),
        DateTime d => d.TimeOfDay == TimeSpan.Zero
            ? d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            : d.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
        TimeSpan t => t.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture),
        double n when n == Math.Floor(n) && Math.Abs(n) < 1e15 => ((long)n).ToString(CultureInfo.InvariantCulture),
        double n => n.ToString("R", CultureInfo.InvariantCulture),
        bool b => b ? "sim" : "não",
        _ => Limpo(Convert.ToString(valor, CultureInfo.InvariantCulture)),
    };

    private static string? Limpo(string? texto)
    {
        var limpo = texto?.Trim();
        return string.IsNullOrEmpty(limpo) ? null : limpo;
    }

    private static XmlReaderSettings ConfiguracaoXmlSegura() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit, // XXE e "bilhão de risadas"
        XmlResolver = null,
        MaxCharactersFromEntities = 0,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
    };

    private static bool Comeca(byte[] conteudo, ReadOnlySpan<byte> assinatura) => conteudo.AsSpan().StartsWith(assinatura);

    private static RegraDeNegocioException MuitasLinhas() =>
        Recusa($"O arquivo passa de {LimiteDeLinhas:N0} linhas. Divida em partes menores.");

    private static RegraDeNegocioException Recusa(string mensagem) => new("RN15", mensagem);
}
