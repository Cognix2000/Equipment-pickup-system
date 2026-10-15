using Retirada.Comum;
using Retirada.Ordens.Dominio;
using Shouldly;

namespace Retirada.Ordens.Testes;

/// <summary>RN10: tentativas de contato com o cliente e a janela de horário.</summary>
public class ContatoTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 14, 14, 0, 0, TimeSpan.Zero); // quarta, 11h em Brasília

    private static Ordem Nova() => Ordem.Importar(
        "os-9301", 5301, "REMOÇÃO EQUIP FTTH", Guid.NewGuid(),
        new Endereco("RUA DOS IPÊS", "120", null, "CENTRO", "CIDADE SIMULADA", "MG", null),
        null, null, [Equipamento.Criar(TipoEquipamento.ONU, "Huawei EG8010H", NumeroSerie.Criar("HWTC9A8B7C6D"))],
        Agora, TimeSpan.FromHours(48));

    private static Ordem Disponivel()
    {
        var o = Nova();
        o.Liberar(Agora);
        return o;
    }

    private static void DeveViolar(Action acao) =>
        Should.Throw<RegraDeNegocioException>(acao).Codigo.ShouldBe("RN10");

    [Theory]
    [InlineData(CanalContato.Ligacao, ResultadoContato.Atendeu)]
    [InlineData(CanalContato.Ligacao, ResultadoContato.NaoAtendeu)]
    [InlineData(CanalContato.Ligacao, ResultadoContato.CaixaPostal)]
    [InlineData(CanalContato.Ligacao, ResultadoContato.NumeroInexistente)]
    [InlineData(CanalContato.Whatsapp, ResultadoContato.Enviada)]
    [InlineData(CanalContato.Whatsapp, ResultadoContato.SemWhatsapp)]
    [InlineData(CanalContato.Whatsapp, ResultadoContato.NaoEntregue)]
    [InlineData(CanalContato.Whatsapp, ResultadoContato.Respondeu)]
    public void Registra_o_contato_sem_mudar_a_situacao(CanalContato canal, ResultadoContato resultado)
    {
        var ordem = Disponivel();
        var versao = ordem.Versao;

        ordem.RegistrarContato(canal, resultado, null, Agora.AddHours(1));

        ordem.Status.ShouldBe(StatusOrdem.Disponivel);
        ordem.Versao.ShouldBe(versao + 1);
        ordem.AtualizadoEm.ShouldBe(Agora.AddHours(1));
        ordem.Eventos[^1].ShouldBe(new EventoDaOrdem($"contato:{resultado}", Agora.AddHours(1)));
    }

    [Theory]
    [InlineData(CanalContato.Ligacao, ResultadoContato.Enviada)]
    [InlineData(CanalContato.Ligacao, ResultadoContato.Respondeu)]
    [InlineData(CanalContato.Whatsapp, ResultadoContato.Atendeu)]
    [InlineData(CanalContato.Whatsapp, ResultadoContato.NumeroInexistente)]
    public void Resultado_tem_de_ser_do_canal(CanalContato canal, ResultadoContato resultado) =>
        DeveViolar(() => Disponivel().RegistrarContato(canal, resultado, null, Agora));

    [Fact]
    public void Modelo_de_mensagem_so_no_WhatsApp()
    {
        DeveViolar(() => Disponivel().RegistrarContato(CanalContato.Ligacao, ResultadoContato.Atendeu, ModeloDeMensagem.Aviso, Agora));
        Should.NotThrow(() => Disponivel().RegistrarContato(CanalContato.Whatsapp, ResultadoContato.Enviada, ModeloDeMensagem.Aviso, Agora));
    }

    [Fact]
    public void Com_o_tecnico_tambem_aceita_contato()
    {
        var ordem = Disponivel();
        ordem.Designar(Guid.NewGuid(), Agora);
        Should.NotThrow(() => ordem.RegistrarContato(CanalContato.Ligacao, ResultadoContato.Atendeu, null, Agora));
    }

    [Fact]
    public void Em_carencia_ou_encerrada_nao_aceita_contato()
    {
        DeveViolar(() => Nova().RegistrarContato(CanalContato.Ligacao, ResultadoContato.Atendeu, null, Agora));

        var cancelada = Disponivel();
        cancelada.Cancelar(MotivoCancelamento.Pagou, Agora);
        DeveViolar(() => cancelada.RegistrarContato(CanalContato.Ligacao, ResultadoContato.Atendeu, null, Agora));
    }

    [Fact]
    public void Contato_barrado_nao_gera_evento()
    {
        var ordem = Nova();
        var eventos = ordem.Eventos.Count;
        Should.Throw<RegraDeNegocioException>(() => ordem.RegistrarContato(CanalContato.Ligacao, ResultadoContato.Atendeu, null, Agora));
        ordem.Eventos.Count.ShouldBe(eventos);
        ordem.Versao.ShouldBe(1);
    }

    [Theory]
    [InlineData(ResultadoContato.Atendeu, true)]
    [InlineData(ResultadoContato.Respondeu, true)]
    [InlineData(ResultadoContato.NaoAtendeu, false)]
    [InlineData(ResultadoContato.CaixaPostal, false)]
    [InlineData(ResultadoContato.Enviada, false)]
    [InlineData(ResultadoContato.NaoEntregue, false)]
    public void So_atender_ou_responder_conta_como_resposta(ResultadoContato resultado, bool respondeu) =>
        Ordem.ClienteRespondeu(resultado).ShouldBe(respondeu);

    [Theory]
    [InlineData("2026-10-14T10:59:00Z", false)] // quarta, 7h59 em Brasília
    [InlineData("2026-10-14T11:00:00Z", true)]  // quarta, 8h
    [InlineData("2026-10-14T20:59:00Z", true)]  // quarta, 17h59
    [InlineData("2026-10-14T21:00:00Z", false)] // quarta, 18h
    [InlineData("2026-10-17T14:00:00Z", true)]  // sábado, 11h
    [InlineData("2026-10-18T14:00:00Z", false)] // domingo, 11h
    [InlineData("2026-10-19T02:30:00Z", false)] // domingo, 23h30 em Brasília (segunda em UTC)
    public void Janela_de_contato_e_no_horario_de_Brasilia(string instante, bool permite) => // RN10
        JanelaDeContato.Permite(DateTimeOffset.Parse(instante)).ShouldBe(permite);
}
