using System.Globalization;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Retirada.Comum;
using Retirada.Ordens.Dominio;

namespace Retirada.Ordens.Importacao;

internal enum MotivoDePendencia { BloqueioTemporario, SemRescisao }

/// <summary>RN05: telefone padronizado em E.164; fora do padrão entra com alerta, sem bloquear a ordem.</summary>
internal sealed record TelefoneLido(string ComoVeio, string NumeroE164, string Tipo, bool FormatoValido);

internal sealed record EquipamentoLido(TipoEquipamento Tipo, string Modelo, NumeroSerie? Serie, string? Mac, decimal? Valor);

/// <summary>O que uma linha da planilha diz sobre a OS, já validado e normalizado.</summary>
internal sealed record OrdemLida(
    string Chave,
    string Cliente,
    int? CodigoCliente,
    string TipoOs,
    MotivoDePendencia? Pendencia,
    Endereco Endereco,
    string? Cep,
    string? Observacao,
    Localizacao? Localizacao,
    int? NumeroOs,
    ReferenciasDaImportacao Referencias,
    IReadOnlyList<TelefoneLido> Telefones,
    EquipamentoLido? Equipamento)
{
    /// <summary>Duas linhas são da mesma OS quando estes campos batem; o equipamento pode mudar de uma para outra.</summary>
    public object DadosDaOs => (Cliente, TipoOs, Endereco, Observacao, NumeroOs);

    public DadosDaImportacao ParaAtualizar() =>
        new(Endereco, Cep, Observacao, NumeroOs, Referencias.DataProgramada, Referencias.DataRescisao, Localizacao);

    public string TipoPessoa => Regex.IsMatch(Cliente, @"\b(LTDA|EIRELI|EPP|MEI|ME|S\.?A\.?|S/A|CIA)\b", RegexOptions.IgnoreCase) ? "PJ" : "PF";
}

internal sealed record LinhaInterpretada(int Numero, OrdemLida? Ordem, IReadOnlyList<string> Erros, IReadOnlyList<string> Alertas, string? MotivoIgnorada = null)
{
    public bool TemErro => Erros.Count > 0;
    /// <summary>
    /// Linha pulada silenciosamente — típico do HubSoft que exporta tipos misturados (REMOÇÃO junto com REATIVAÇÃO,
    /// INSTALAÇÃO, MANUTENÇÃO). Diferente de erro: não precisa de atenção do financeiro, só é contada na prévia.
    /// </summary>
    public bool FoiIgnorada => MotivoIgnorada is not null;
}

/// <summary>Valida e normaliza linha a linha (UC01). Erro tira a linha da importação; alerta só avisa.</summary>
internal static partial class Interpretador
{
    // O Brasil não tem horário de verão desde 2019: a hora da planilha é sempre UTC-3.
    private static readonly TimeSpan FusoDeBrasilia = TimeSpan.FromHours(-3);
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static LinhaInterpretada Interpretar(LinhaLida linha, MapaDeColunas mapa, bool modoSimulacao)
    {
        var erros = new List<string>();
        var alertas = new List<string>();
        string? V(Campo c) => mapa.Valor(linha, c) is { } v ? EspacosRepetidos().Replace(v, " ") : null;

        // RN02 + HubSoft real (nov/2026): a planilha vem com todos os tipos de OS misturados (REMOÇÃO, REATIVAÇÃO,
        // INSTALAÇÃO, MANUTENÇÃO...). Linha que não é de remoção, retirada ou bloqueio não interessa ao sistema:
        // pula sem erro, sem alerta — só conta na prévia. Isso tem que vir ANTES das validações de cliente/endereço
        // para não entupir a aba de erros com linhas que nem deveriam ter sido olhadas.
        var tipoOsLido = V(Campo.TipoOs);
        if (tipoOsLido is not null)
        {
            var tipoNormalizado = SemAcento(tipoOsLido).ToUpperInvariant();
            var ehDeRemocao = tipoNormalizado.Contains("REMOC") || tipoNormalizado.Contains("RETIRADA") || tipoNormalizado.Contains("BLOQUEIO");
            if (!ehDeRemocao)
                return new LinhaInterpretada(linha.Numero, null, [], [], MotivoIgnorada: $"tipo \"{tipoOsLido}\" não é de remoção");
        }

        // RF04 revisado (nov/2026): só cliente, logradouro e cidade são obrigatórios. O resto ganha padrão e alerta
        // na prévia, para o financeiro ver e decidir. Planilhas vêm de mil jeitos; a importação não deveria barrar
        // por falta de telefone ou UF.
        var cliente = Obrigatorio(V(Campo.Cliente), "falta o cliente", erros);
        var logradouro = Obrigatorio(V(Campo.Logradouro), "falta o logradouro (endereço)", erros);
        var cidade = Obrigatorio(V(Campo.Cidade), "falta a cidade", erros);
        var bairro = V(Campo.Bairro);
        if (bairro is null) { bairro = "(não informado)"; alertas.Add("sem bairro: entrou como \"(não informado)\""); }
        var uf = V(Campo.Uf)?.ToUpperInvariant();
        if (uf is not null && !Regex.IsMatch(uf, "^[A-Z]{2}$")) { alertas.Add($"UF fora do padrão: {uf}"); uf = null; }
        if (uf is null) { uf = ""; alertas.Add("sem UF: aparece vazio na ordem"); }
        var numero = V(Campo.Numero);
        if (numero is null) { numero = "s/n"; alertas.Add("sem número no endereço: entrou como \"s/n\""); }

        // Sem tipo na planilha, o padrão é retirada de equipamento (que é o que o sistema faz).
        var tipoOs = tipoOsLido;
        if (tipoOs is null) { tipoOs = "RETIRADA DE EQUIPAMENTO"; alertas.Add("sem tipo de OS: entrou como \"RETIRADA DE EQUIPAMENTO\""); }
        MotivoDePendencia? pendencia = null;
        var tipo = SemAcento(tipoOs).ToUpperInvariant();
        if (tipo.Contains("BLOQUEIO")) pendencia = MotivoDePendencia.BloqueioTemporario;
        if (pendencia is null && V(Campo.SituacaoDoServico) is { } situacao && !SemAcento(situacao).Contains("CANCEL", StringComparison.OrdinalIgnoreCase))
            pendencia = MotivoDePendencia.SemRescisao;

        var dataProgramada = DataEHora(V(Campo.DataProgramada), V(Campo.HoraProgramada));
        if (mapa.Tem(Campo.DataProgramada) && V(Campo.DataProgramada) is not null && dataProgramada is null)
            alertas.Add("data ou hora programada fora do formato dd/mm/aaaa hh:mm");
        var dataRescisao = Data(V(Campo.DataRescisao));
        if (V(Campo.DataRescisao) is not null && dataRescisao is null) alertas.Add("data da rescisão fora do formato dd/mm/aaaa");

        var idOs = Inteiro(V(Campo.IdOrdemServico), "id da OS", erros);
        var protocolo = V(Campo.Protocolo);
        if (protocolo is not null && !Regex.IsMatch(protocolo, "^[0-9]{20}$"))
        {
            erros.Add("protocolo inválido: precisa ter 20 números (formate a coluna como texto)");
            protocolo = null;
        }
        var numeroOs = Inteiro(V(Campo.NumeroOs), "nº da OS", alertas);
        var codigoCliente = Inteiro(V(Campo.CodigoCliente), "código do cliente", alertas);
        var idClienteServico = Inteiro(V(Campo.IdClienteServico), "id do serviço do cliente", alertas);

        // RN01: id da OS, depois protocolo; sem eles, a chave provisória junta cliente, endereço e data (programada
        // ou rescisão). Se nem data vier, cai na chave de cliente+endereço+cidade: reimportar a mesma linha não
        // duplica, mas duas rescisões do mesmo cliente no mesmo endereço precisarão de data ou protocolo para serem
        // tratadas como ordens diferentes.
        string? chave = idOs is not null ? $"os:{idOs}" : protocolo is not null ? $"protocolo:{protocolo}" : null;
        if (chave is null && !erros.Any(e => e.StartsWith("protocolo")) && cliente is not null && logradouro is not null && cidade is not null)
        {
            if (dataProgramada is { } dp) chave = "provisoria:" + Resumo($"{cliente}|{logradouro}|{numero}|{dp:O}");
            else if (dataRescisao is { } dr) chave = "provisoria:" + Resumo($"{cliente}|{logradouro}|{numero}|{dr:O}");
            else { chave = "provisoria:" + Resumo($"{cliente}|{logradouro}|{numero}|{bairro}|{cidade}|{uf}"); alertas.Add("sem protocolo nem id da OS: reimportar a mesma linha não duplica, mas uma segunda ordem para o mesmo endereço vai precisar de data ou protocolo"); }
        }

        var cep = V(Campo.Cep) is { } textoCep ? Cep(textoCep, alertas) : null;
        var localizacao = Localizacao(V(Campo.Latitude), V(Campo.Longitude), mapa.Tem(Campo.Latitude) && mapa.Tem(Campo.Longitude), alertas);

        var telefones = new List<TelefoneLido>();
        foreach (var campo in new[] { Campo.Telefone1, Campo.Telefone2, Campo.Telefone3 })
            if (V(campo) is { } texto && Telefone(texto, modoSimulacao) is { } telefone)
            {
                telefones.Add(telefone);
                if (!telefone.FormatoValido) alertas.Add($"telefone fora do padrão: {texto}");
            }

        // HubSoft real: as colunas de telefone vêm vazias e o contato está dentro do texto livre
        // (`TELEFONE PARA CONTATO: (37) 99951-3096`). Só vai nessa extração se nada válido saiu das colunas próprias.
        // Todo o resto do texto livre é descartado (RN04).
        if (!telefones.Any(t => t.FormatoValido) && V(Campo.DescricaoLivre) is { } textoLivre)
            foreach (var bruto in TelefonesNaDescricao().Matches(textoLivre).Select(m => m.Value).Distinct())
                if (Telefone(bruto, modoSimulacao) is { FormatoValido: true } telefoneDoTexto)
                    telefones.Add(telefoneDoTexto);

        if (!telefones.Any(t => t.FormatoValido)) alertas.Add("sem telefone válido: o contato fica só por visita");

        var equipamento = Equipamento(V, erros, alertas);

        if (erros.Count > 0 || chave is null)
            return new LinhaInterpretada(linha.Numero, null, erros, alertas);

        var ordem = new OrdemLida(
            chave, cliente!, codigoCliente, tipoOs!, pendencia,
            new Endereco(logradouro!, numero, V(Campo.Complemento), bairro!, cidade!, uf!, V(Campo.Referencia)),
            cep, V(Campo.Observacao), localizacao, numeroOs,
            new ReferenciasDaImportacao(protocolo, idOs, idClienteServico, dataProgramada, dataRescisao, cep),
            telefones, equipamento);
        return new LinhaInterpretada(linha.Numero, ordem, erros, alertas);
    }

    /// <summary>RN05: só dígitos com DDD (11 no celular, 10 no fixo). DDD 00 só vale no modo simulação.</summary>
    internal static TelefoneLido? Telefone(string comoVeio, bool modoSimulacao)
    {
        var digitos = new string(comoVeio.Where(char.IsAsciiDigit).ToArray());
        if (digitos.Length == 0) return null;
        if (digitos.StartsWith("55") && digitos.Length is 12 or 13) digitos = digitos[2..];

        var ddd = digitos.Length >= 2 ? digitos[..2] : string.Empty;
        var dddValido = Regex.IsMatch(ddd, "^[1-9][1-9]$") || (modoSimulacao && ddd == "00");
        var (valido, tipo) = digitos.Length switch
        {
            11 when dddValido && digitos[2] == '9' => (true, "celular"),
            10 when dddValido && digitos[2] is >= '2' and <= '5' => (true, "fixo"),
            _ => (false, "desconhecido"),
        };
        return new TelefoneLido(comoVeio, "+55" + digitos, tipo, valido);
    }

    private static EquipamentoLido? Equipamento(Func<Campo, string?> v, List<string> erros, List<string> alertas)
    {
        if (v(Campo.EquipamentoTipo) is not { } textoTipo) return null;

        TipoEquipamento? tipo = SemAcento(textoTipo).ToUpperInvariant().Replace(" ", string.Empty) switch
        {
            "ONT" => TipoEquipamento.ONT,
            "ONU" => TipoEquipamento.ONU,
            "ROTEADOR" => TipoEquipamento.Roteador,
            "REPETIDORMESH" or "MESH" => TipoEquipamento.RepetidorMesh,
            "FONTE" => TipoEquipamento.Fonte,
            _ => null,
        };
        if (tipo is null) { erros.Add($"tipo de equipamento desconhecido: {textoTipo}"); return null; }

        var modelo = Obrigatorio(v(Campo.EquipamentoModelo), "falta o modelo do equipamento", erros);
        NumeroSerie? serie = null;
        if (v(Campo.NumeroSerie) is { } textoSerie)
        {
            try { serie = NumeroSerie.Criar(textoSerie); }
            catch (RegraDeNegocioException) { erros.Add("número de série inválido"); }
        }
        else if (tipo != TipoEquipamento.Fonte) erros.Add($"falta o número de série do {textoTipo}");

        string? mac = null;
        if (v(Campo.Mac) is { } textoMac)
        {
            if (PhysicalAddress.TryParse(textoMac.Replace(':', '-').ToUpperInvariant(), out var endereco) && endereco.GetAddressBytes().Length == 6)
                mac = string.Join(':', endereco.GetAddressBytes().Select(b => b.ToString("X2")));
            else alertas.Add($"MAC fora do padrão: {textoMac}");
        }

        decimal? valor = null;
        if (v(Campo.ValorEquipamento) is { } textoValor)
        {
            valor = Valor(textoValor);
            if (valor is null) alertas.Add($"valor do equipamento fora do padrão: {textoValor}");
        }

        return modelo is null || (serie is null && tipo != TipoEquipamento.Fonte) ? null : new EquipamentoLido(tipo.Value, modelo, serie, mac, valor);
    }

    private static Localizacao? Localizacao(string? latitude, string? longitude, bool temColunas, List<string> alertas)
    {
        if (!temColunas) return null;
        if (latitude is null && longitude is null) { alertas.Add("sem localização: aparece só na lista, não no mapa"); return null; }
        if (Decimal(latitude) is { } lat && Decimal(longitude) is { } lon && lat is >= -90 and <= 90 && lon is >= -180 and <= 180)
            return new Localizacao(lat, lon);
        alertas.Add("localização inválida: aparece só na lista, não no mapa");
        return null;
    }

    /// <summary>
    /// Valor em reais. A célula numérica do Excel chega com ponto decimal ("150.5"); o texto digitado vem no formato
    /// brasileiro ("R$ 1.500,00"). Ler tudo como pt-BR tomava o ponto por milhar e transformava 150,50 em 1.505,00.
    /// Regra: com vírgula, ou com ponto só em grupos de três dígitos ("1.500"), é pt-BR; senão, o ponto é decimal.
    /// </summary>
    internal static decimal? Valor(string texto)
    {
        var limpo = texto.Replace("R$", string.Empty).Replace("\u00A0", string.Empty).Replace(" ", string.Empty);
        var cultura = limpo.Contains(',') || PontoDeMilhar().IsMatch(limpo) ? PtBr : CultureInfo.InvariantCulture;
        return decimal.TryParse(limpo, NumberStyles.Number, cultura, out var valor) && valor >= 0 ? valor : null;
    }

    [GeneratedRegex(@"^\d{1,3}(\.\d{3})+$")]
    private static partial Regex PontoDeMilhar();

    private static double? Decimal(string? texto) =>
        double.TryParse(texto?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static string? Cep(string texto, List<string> alertas)
    {
        var digitos = new string(texto.Where(char.IsAsciiDigit).ToArray());
        if (digitos.Length == 8) return $"{digitos[..5]}-{digitos[5..]}";
        alertas.Add($"CEP fora do padrão: {texto}");
        return null;
    }

    private static int? Inteiro(string? texto, string nome, List<string> problemas)
    {
        if (texto is null) return null;
        if (int.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0) return n;
        problemas.Add($"{nome} inválido: {texto}");
        return null;
    }

    private static readonly string[] FormatosDeData = ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm", "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss"];

    internal static DateOnly? Data(string? texto) =>
        texto is not null && DateTime.TryParseExact(texto, FormatosDeData, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? DateOnly.FromDateTime(d) : null;

    /// <summary>"15/09/2026" + "09:00:00" → 15/09/2026 09:00 em Brasília. A hora vinda do Excel pode chegar com uma data qualquer na frente.</summary>
    internal static DateTimeOffset? DataEHora(string? data, string? hora)
    {
        if (Data(data) is not { } dia) return null;
        var horario = TimeSpan.Zero;
        if (hora is not null)
        {
            var soHora = hora.Contains(' ') ? hora[(hora.LastIndexOf(' ') + 1)..] : hora;
            if (!TimeSpan.TryParseExact(soHora, [@"hh\:mm\:ss", @"h\:mm\:ss", @"hh\:mm", @"h\:mm"], CultureInfo.InvariantCulture, out horario)) return null;
        }
        else if (data!.Contains(' ') && DateTime.TryParseExact(data, FormatosDeData, CultureInfo.InvariantCulture, DateTimeStyles.None, out var completo))
        {
            horario = completo.TimeOfDay;
        }
        // Gravado em UTC (o PostgreSQL guarda o instante); a tela mostra de novo no horário de Brasília.
        return new DateTimeOffset(dia.ToDateTime(TimeOnly.FromTimeSpan(horario)), FusoDeBrasilia).ToUniversalTime();
    }

    private static string? Obrigatorio(string? valor, string erro, List<string> erros)
    {
        if (valor is null) erros.Add(erro);
        return valor;
    }

    private static string Resumo(string texto) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(SemAcento(texto).ToUpperInvariant())))[..32];

    private static string SemAcento(string texto) =>
        string.Concat(texto.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark));

    [GeneratedRegex(@"\s+")]
    private static partial Regex EspacosRepetidos();

    /// <summary>
    /// Captura telefones brasileiros avulsos num texto livre. Aceita (37) 99951-3096, 37 99951 3096, 37998365063,
    /// com ou sem o dígito 9, com +55 opcional, inclusive DDD 00 em modo simulação. A validação final (DDD, 9º
    /// dígito, DDD válido) vai no `Telefone()` — aqui o papel é só pegar candidatos; falsos positivos são
    /// descartados lá e nunca entram na ordem.
    /// </summary>
    [GeneratedRegex(@"(?:\+?55\s*)?\(?\s*\d{2}\s*\)?\s*9?\s*\d{4}[\s-]?\d{4}")]
    private static partial Regex TelefonesNaDescricao();
}
