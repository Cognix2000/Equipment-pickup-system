using Retirada.Comum;

namespace Retirada.Ordens.Dominio;

/// <summary>
/// Raiz do agregado. Toda mudança de estado passa por um método daqui, que aplica a regra
/// e sobe a <see cref="Versao"/> (concorrência otimista: dois aceites ao mesmo tempo, só um vence).
/// </summary>
public sealed class Ordem : Entidade
{
    private static readonly StatusOrdem[] Encerrados =
        [StatusOrdem.Conferida, StatusOrdem.Cancelada, StatusOrdem.DevolvidaNaLoja, StatusOrdem.EncerradaSemRetirada];

    private static readonly StatusOrdem[] ComTecnico = [StatusOrdem.Atribuida, StatusOrdem.EmRota];

    /// <summary>Depois da carência e antes da retirada: é quando se combina a visita com o cliente.</summary>
    private static readonly StatusOrdem[] AceitamContato = [StatusOrdem.Disponivel, StatusOrdem.Atribuida, StatusOrdem.EmRota];

    private static readonly ResultadoContato[] DaLigacao =
        [ResultadoContato.Atendeu, ResultadoContato.NaoAtendeu, ResultadoContato.CaixaPostal, ResultadoContato.NumeroInexistente];

    private readonly List<Equipamento> _equipamentos = [];
    private readonly List<EventoDaOrdem> _eventos = [];

    // Stryker disable once String : valor inicial só para o EF Core; a fábrica sempre preenche
    public string ChaveImportacao { get; private set; } = string.Empty;
    public int? NumeroOs { get; private set; }
    // Stryker disable once String : valor inicial só para o EF Core; a fábrica sempre preenche
    public string TipoOs { get; private set; } = string.Empty;
    public Guid ClienteId { get; private set; }
    public Endereco Endereco { get; private set; } = null!;
    public Localizacao? Localizacao { get; private set; }
    public string? Observacao { get; private set; }
    public StatusOrdem Status { get; private set; }
    public DateTimeOffset CarenciaAte { get; private set; }
    public Guid? ResponsavelId { get; private set; }
    public short VisitasSemSucesso { get; private set; }
    public int Versao { get; private set; } = 1;
    public DateTimeOffset CriadoEm { get; private set; }
    public DateTimeOffset AtualizadoEm { get; private set; }
    public DateTimeOffset? EncerradaEm { get; private set; }
    public DateTimeOffset? BaixaHubsoftEm { get; private set; }
    public Guid? BaixaHubsoftPor { get; private set; }

    // Referências da exportação do HubSoft (vazias enquanto a planilha não trouxer).
    public string? Protocolo { get; private set; }
    public int? IdOrdemServicoHubsoft { get; private set; }
    public int? IdClienteServicoHubsoft { get; private set; }
    public DateTimeOffset? DataProgramada { get; private set; }
    public DateOnly? DataRescisao { get; private set; }
    public string? Cep { get; private set; }
    public Guid? ImportacaoId { get; private set; }

    public IReadOnlyList<Equipamento> Equipamentos => _equipamentos;

    /// <summary>Eventos gerados nesta sessão, para gravar em evento_ordem (só acréscimo, RN12).</summary>
    public IReadOnlyList<EventoDaOrdem> Eventos => _eventos;

    /// <summary>RF19: retirada registrada ou conferida, e o financeiro ainda não marcou a baixa.</summary>
    public bool AguardaBaixaNoHubsoft =>
        Status is StatusOrdem.Concluida or StatusOrdem.Conferida && BaixaHubsoftEm is null;

    private Ordem() { } // EF Core

    /// <summary>Ordem nasce da importação, como Nova, em carência contada da importação (RN03).</summary>
    public static Ordem Importar(
        string chaveImportacao,
        int? numeroOs,
        string tipoOs,
        Guid clienteId,
        Endereco endereco,
        Localizacao? localizacao,
        string? observacao,
        IEnumerable<Equipamento> equipamentos,
        DateTimeOffset agora,
        TimeSpan carencia,
        ReferenciasDaImportacao? referencias = null)
    {
        if (string.IsNullOrWhiteSpace(chaveImportacao))
            throw new RegraDeNegocioException("RN01", "A ordem precisa de uma chave de importação.");

        var ordem = new Ordem
        {
            ChaveImportacao = chaveImportacao,
            NumeroOs = numeroOs,
            TipoOs = tipoOs,
            ClienteId = clienteId,
            Endereco = endereco,
            Localizacao = localizacao,
            Observacao = observacao,
            Status = StatusOrdem.Nova,
            CarenciaAte = agora + carencia,
            CriadoEm = agora,
            AtualizadoEm = agora,
            Protocolo = referencias?.Protocolo,
            IdOrdemServicoHubsoft = referencias?.IdOrdemServico,
            IdClienteServicoHubsoft = referencias?.IdClienteServico,
            DataProgramada = referencias?.DataProgramada,
            DataRescisao = referencias?.DataRescisao,
            Cep = referencias?.Cep,
            ImportacaoId = referencias?.ImportacaoId,
        };
        ordem._equipamentos.AddRange(equipamentos);
        ordem.Registrar("importada", agora);
        return ordem;
    }

    /// <summary>
    /// RN01: o que a planilha nova mudou nesta ordem (nomes dos campos), sem alterar nada.
    /// A prévia mostra a lista; a confirmação aplica com <see cref="AtualizarPelaImportacao"/>.
    /// </summary>
    public IReadOnlyList<string> MudancasDaImportacao(DadosDaImportacao novos)
    {
        var mudancas = new List<string>();
        if (novos.Endereco != Endereco) mudancas.Add("endereço");
        if (novos.Cep is not null && novos.Cep != Cep) mudancas.Add("CEP");
        if (novos.Observacao != Observacao) mudancas.Add("observação");
        if (novos.NumeroOs is not null && novos.NumeroOs != NumeroOs) mudancas.Add("nº da OS");
        if (novos.DataProgramada is not null && novos.DataProgramada != DataProgramada) mudancas.Add("data programada");
        if (novos.DataRescisao is not null && novos.DataRescisao != DataRescisao) mudancas.Add("data da rescisão");
        if (novos.Localizacao is not null && novos.Localizacao != Localizacao) mudancas.Add("localização");
        return mudancas;
    }

    /// <summary>Só ordem que ainda não terminou recebe atualização da planilha; a encerrada fica como está.</summary>
    public bool AceitaAtualizacaoDaImportacao => !Encerrados.Contains(Status);

    /// <summary>RN01: reimportar não duplica, só atualiza o que mudou. Sem mudança, nada acontece.</summary>
    public IReadOnlyList<string> AtualizarPelaImportacao(DadosDaImportacao novos, DateTimeOffset agora)
    {
        Exigir(AceitaAtualizacaoDaImportacao, "RN01", "Ordem encerrada não é atualizada pela importação.");
        var mudancas = MudancasDaImportacao(novos);
        if (mudancas.Count == 0) return mudancas;

        Endereco = novos.Endereco;
        Cep = novos.Cep ?? Cep;
        Observacao = novos.Observacao;
        NumeroOs = novos.NumeroOs ?? NumeroOs;
        DataProgramada = novos.DataProgramada ?? DataProgramada;
        DataRescisao = novos.DataRescisao ?? DataRescisao;
        Localizacao = novos.Localizacao ?? Localizacao;
        Tocar(agora);
        Registrar("atualizada_pela_importacao", agora);
        return mudancas;
    }

    /// <summary>O financeiro libera antes do fim da carência (RN03).</summary>
    public void Liberar(DateTimeOffset agora)
    {
        Exigir(Status == StatusOrdem.Nova, "RN03", "Só ordem Nova pode ser liberada.");
        Mudar(StatusOrdem.Disponivel, "liberada", agora);
    }

    /// <summary>Rotina periódica: fim da carência torna a ordem Disponível.</summary>
    public bool LiberarSeCarenciaVenceu(DateTimeOffset agora)
    {
        if (Status != StatusOrdem.Nova || agora < CarenciaAte) return false;
        Mudar(StatusOrdem.Disponivel, "carencia_vencida", agora);
        return true;
    }

    /// <summary>RF08: qualquer técnico recebe ordem de qualquer bairro.</summary>
    public void Designar(Guid tecnicoId, DateTimeOffset agora)
    {
        Exigir(Status == StatusOrdem.Disponivel, "RF08", "Só ordem Disponível pode ser designada.");
        ResponsavelId = tecnicoId;
        Mudar(StatusOrdem.Atribuida, "designada", agora);
    }

    /// <summary>
    /// RF12/UC04. <paramref name="seriesLidas"/> traz um item por equipamento recolhido
    /// (fonte com valor nulo). Série diferente fica como divergência para o estoque.
    /// </summary>
    public void RegistrarRetirada(
        IReadOnlyDictionary<Guid, NumeroSerie?> seriesLidas,
        Localizacao local,
        DateTimeOffset agora)
    {
        Exigir(Status != StatusOrdem.Cancelada, "RN07", "A ordem foi cancelada: a retirada está bloqueada.");
        Exigir(ComTecnico.Contains(Status), "RF12", "Só ordem atribuída a um técnico pode ser retirada.");

        var faltando = _equipamentos.Where(e => !seriesLidas.ContainsKey(e.Id)).ToList();
        Exigir(faltando.Count == 0, "RF12",
            $"Faltam itens na retirada: {string.Join(", ", faltando.Select(e => e.Modelo))}.");

        foreach (var equipamento in _equipamentos)
            equipamento.RegistrarRetirada(seriesLidas[equipamento.Id]);

        Localizacao = Localizacao ?? local;
        Mudar(StatusOrdem.Concluida, "retirada", agora);
    }

    /// <summary>RF13 e RN09: no limite de visitas, encerra sem retirada; antes disso, volta para Disponível.</summary>
    public void RegistrarVisitaSemSucesso(MotivoVisitaSemSucesso motivo, int limiteVisitas, DateTimeOffset agora)
    {
        Exigir(ComTecnico.Contains(Status), "RF13", "Só ordem atribuída a um técnico recebe visita.");

        VisitasSemSucesso++;
        ResponsavelId = null;
        var destino = VisitasSemSucesso >= limiteVisitas ? StatusOrdem.EncerradaSemRetirada : StatusOrdem.Disponivel;
        Mudar(destino, $"visita_sem_sucesso:{motivo}", agora);
    }

    /// <summary>
    /// RF14 e RN08: confere item a item. Com divergência, a ordem continua Concluída
    /// e as divergências voltam para a tela do estoque.
    /// </summary>
    public ResultadoConferencia Conferir(
        IReadOnlyDictionary<Guid, NumeroSerie?> seriesLidas,
        Guid conferidoPor,
        DateTimeOffset agora)
    {
        Exigir(Status == StatusOrdem.Concluida, "RF14", "Só ordem com retirada registrada pode ser conferida.");

        var divergentes = new List<Equipamento>();
        foreach (var equipamento in _equipamentos)
        {
            seriesLidas.TryGetValue(equipamento.Id, out var lida);
            if (!equipamento.Conferir(lida, conferidoPor, agora))
                divergentes.Add(equipamento);
        }

        if (divergentes.Count > 0)
        {
            Tocar(agora);
            Registrar("divergencia_na_conferencia", agora);
            return new ResultadoConferencia(false, divergentes);
        }

        Mudar(StatusOrdem.Conferida, "conferida", agora);
        return new ResultadoConferencia(true, []);
    }

    /// <summary>RF09: exige motivo; a ordem sai da lista do técnico.</summary>
    public void Cancelar(MotivoCancelamento motivo, DateTimeOffset agora)
    {
        Exigir(Status is StatusOrdem.Nova or StatusOrdem.Disponivel or StatusOrdem.Atribuida
                   or StatusOrdem.EmRota or StatusOrdem.NaoConcluida,
            "RF09", "Ordem com equipamento já retirado ou encerrada não pode ser cancelada.");
        ResponsavelId = null;
        Mudar(StatusOrdem.Cancelada, $"cancelada:{motivo}", agora);
    }

    /// <summary>
    /// RN10: registra uma tentativa de contato com o cliente (ligação ou WhatsApp) e o resultado.
    /// O registro é a base do prazo de dias sem resposta; a ordem não muda de situação.
    /// </summary>
    public void RegistrarContato(CanalContato canal, ResultadoContato resultado, ModeloDeMensagem? modelo, DateTimeOffset agora)
    {
        Exigir(AceitamContato.Contains(Status), "RN10",
            "Contato só com ordem em andamento: depois da carência e antes da retirada.");
        var ehLigacao = canal == CanalContato.Ligacao;
        Exigir(DaLigacao.Contains(resultado) == ehLigacao, "RN10",
            ehLigacao ? "Esse resultado não é de ligação." : "Esse resultado não é de mensagem de WhatsApp.");
        Exigir(!ehLigacao || modelo is null, "RN10", "Ligação não tem modelo de mensagem.");
        Tocar(agora);
        Registrar($"contato:{resultado}", agora);
    }

    /// <summary>O cliente deu sinal: atendeu a ligação ou respondeu a mensagem.</summary>
    public static bool ClienteRespondeu(ResultadoContato resultado) =>
        resultado is ResultadoContato.Atendeu or ResultadoContato.Respondeu;

    /// <summary>RF15 e RN13: vale até depois de encerrada sem retirada.</summary>
    public void RegistrarDevolucaoNaLoja(DateTimeOffset agora)
    {
        Exigir(Status is not (StatusOrdem.Conferida or StatusOrdem.Cancelada or StatusOrdem.DevolvidaNaLoja),
            "RF15", "Essa ordem já foi encerrada.");
        foreach (var equipamento in _equipamentos)
            equipamento.RegistrarDevolucaoNaLoja();
        ResponsavelId = null;
        Mudar(StatusOrdem.DevolvidaNaLoja, "devolvida_na_loja", agora);
    }

    /// <summary>RF19: o financeiro marca que deu a baixa no HubSoft.</summary>
    public void MarcarBaixaFeita(Guid usuarioId, DateTimeOffset agora)
    {
        Exigir(AguardaBaixaNoHubsoft, "RF19", "Essa ordem não está na lista para dar baixa.");
        BaixaHubsoftEm = agora;
        BaixaHubsoftPor = usuarioId;
        Tocar(agora);
        Registrar("baixa_hubsoft", agora);
    }

    private void Mudar(StatusOrdem novo, string evento, DateTimeOffset agora)
    {
        Status = novo;
        EncerradaEm = Encerrados.Contains(novo) ? agora : null;
        Tocar(agora);
        Registrar(evento, agora);
    }

    private void Tocar(DateTimeOffset agora)
    {
        AtualizadoEm = agora;
        Versao++;
    }

    private void Registrar(string tipo, DateTimeOffset agora) => _eventos.Add(new EventoDaOrdem(tipo, agora));

    private static void Exigir(bool condicao, string codigo, string mensagem)
    {
        if (!condicao) throw new RegraDeNegocioException(codigo, mensagem);
    }
}

public sealed record EventoDaOrdem(string Tipo, DateTimeOffset OcorridoEm);

/// <summary>Identificadores e datas que a exportação do HubSoft traz junto com a OS.</summary>
public sealed record ReferenciasDaImportacao(
    string? Protocolo = null,
    int? IdOrdemServico = null,
    int? IdClienteServico = null,
    DateTimeOffset? DataProgramada = null,
    DateOnly? DataRescisao = null,
    string? Cep = null,
    Guid? ImportacaoId = null);

/// <summary>O que uma reimportação pode atualizar numa ordem que já existe (RN01).</summary>
public sealed record DadosDaImportacao(
    Endereco Endereco,
    string? Cep,
    string? Observacao,
    int? NumeroOs,
    DateTimeOffset? DataProgramada,
    DateOnly? DataRescisao,
    Localizacao? Localizacao);

public sealed record ResultadoConferencia(bool Fechou, IReadOnlyList<Equipamento> Divergentes);
