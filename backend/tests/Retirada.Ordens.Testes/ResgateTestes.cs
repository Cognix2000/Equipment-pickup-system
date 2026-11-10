using System.Text.Json;
using Retirada.Comum;
using Retirada.Ordens.Casos;
using Retirada.Ordens.Dominio;
using Retirada.Ordens.Infraestrutura.Pagamentos;
using Retirada.Ordens.Portas;
using Shouldly;

namespace Retirada.Ordens.Testes;

/// <summary>Regras do resgate por PIX e da chave PIX (só dados fictícios: CPF e CNPJ de exemplo dos manuais, DDD 00).</summary>
public class ResgateTestes
{
    private static string Codigo(Action acao) => Should.Throw<RegraDeNegocioException>(acao).Codigo;

    [Theory]
    [InlineData("cpf", "123.456.789-09", "12345678909")]
    [InlineData("cnpj", "11.222.333/0001-81", "11222333000181")]
    [InlineData("email", "  Terceiro.Pix@Exemplo.Invalid ", "terceiro.pix@exemplo.invalid")]
    [InlineData("aleatoria", "0192F000-0000-7000-8000-0000000000AB", "0192f000-0000-7000-8000-0000000000ab")]
    public void Chave_pix_valida_sai_normalizada(string tipo, string chave, string esperada) =>
        ChavePix.Validar(tipo, chave).Chave.ShouldBe(esperada);

    [Theory]
    [InlineData("cpf", "123.456.789-00")]     // dígito verificador errado
    [InlineData("cpf", "111.111.111-11")]     // todos iguais
    [InlineData("cnpj", "11.222.333/0001-80")]
    [InlineData("email", "sem-arroba")]
    [InlineData("telefone", "(00) 1234-5678")] // fixo, não celular
    [InlineData("aleatoria", "nao-e-uuid")]
    [InlineData("conta", "123")]
    public void Chave_pix_fora_do_formato_e_recusada(string tipo, string chave) =>
        Codigo(() => ChavePix.Validar(tipo, chave)).ShouldBe("PIX_CHAVE_INVALIDA");

    [Fact]
    public void Celular_com_ddd_00_so_vale_em_simulacao()
    {
        Codigo(() => ChavePix.Validar("telefone", "(00) 91234-5678")).ShouldBe("PIX_CHAVE_INVALIDA");
        ChavePix.Validar("telefone", "+55 00 91234-5678", modoSimulacao: true).Chave.ShouldBe("+5500912345678");
    }

    [Theory]
    [InlineData("cpf", "12345678909", "***.456.789-**")]
    [InlineData("cnpj", "11222333000181", "**.222.333/****-**")]
    [InlineData("telefone", "+5500912345678", "+5500 *****-5678")]
    [InlineData("email", "terceiro.pix@exemplo.invalid", "te***@exemplo.invalid")]
    [InlineData("aleatoria", "0192f000-0000-7000-8000-0000000000ab", "0192****-****-00ab")]
    public void Chave_mascarada_nao_mostra_o_documento_inteiro(string tipo, string chave, string esperada) =>
        new ChavePix(tipo, chave).Mascarada().ShouldBe(esperada);

    [Fact]
    public void So_o_terceiro_com_chave_aprovada_e_saldo_minimo_resgata_um_por_vez()
    {
        Codigo(() => RegrasDoResgate.ConferirSePodeResgatar(Vinculos.Clt, true, false, 5000, 2000)).ShouldBe("RESGATE_SO_TERCEIRO");
        Codigo(() => RegrasDoResgate.ConferirSePodeResgatar(null, true, false, 5000, 2000)).ShouldBe("RESGATE_SO_TERCEIRO"); // sem vínculo = CLT
        Codigo(() => RegrasDoResgate.ConferirSePodeResgatar(Vinculos.Terceiro, false, false, 5000, 2000)).ShouldBe("RESGATE_SEM_CHAVE");
        Codigo(() => RegrasDoResgate.ConferirSePodeResgatar(Vinculos.Terceiro, true, true, 5000, 2000)).ShouldBe("RESGATE_EM_ANDAMENTO");
        Should.Throw<RegraDeNegocioException>(() => RegrasDoResgate.ConferirSePodeResgatar(Vinculos.Terceiro, true, false, 1500, 2000))
            .Message.ShouldBe("O resgate mínimo é de R$ 20,00. Hoje você tem R$ 15,00 das retiradas já conferidas pelo estoque.");
        RegrasDoResgate.ConferirSePodeResgatar(Vinculos.Terceiro, true, false, 2000, 2000); // no mínimo, passa
    }

    [Theory]
    [InlineData("DONE", "Pago")]
    [InlineData("FAILED", "Falhou")]
    [InlineData("CANCELLED", "Falhou")]
    [InlineData("PENDING", "Pendente")]
    [InlineData("BANK_PROCESSING", "Pendente")]
    public void Le_a_transferencia_do_asaas(string status, string situacao)
    {
        var esperada = Enum.Parse<SituacaoNoGateway>(situacao);
        var json = JsonDocument.Parse($$"""
            {"id":"tra_1","status":"{{status}}","value":25.5,"endToEndIdentifier":"E00000000202611101200abcdefghijk",
             "transactionReceiptUrl":"https://comprovante.exemplo.invalid/1","failReason":null}
            """).RootElement;
        var pix = GatewayAsaas.Ler(json);
        pix.GatewayId.ShouldBe("tra_1");
        pix.Situacao.ShouldBe(esperada);
        pix.IdFimAFim.ShouldBe("E00000000202611101200abcdefghijk");
        if (esperada == SituacaoNoGateway.Falhou) pix.MotivoFalha.ShouldBe("O banco recusou o PIX.");
        GatewayAsaas.Centavos(json, "value").ShouldBe(2550);
    }

    [Fact]
    public void Mes_da_folha_comeca_no_dia_1_em_brasilia()
    {
        FolhaDoMes.InicioDoMes("2026-11", DateTimeOffset.UtcNow).ShouldBe(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(-3)));
        // 01/12 às 01h UTC ainda é 30/11 em Brasília.
        FolhaDoMes.InicioDoMes(null, new DateTimeOffset(2026, 12, 1, 1, 0, 0, TimeSpan.Zero)).ShouldBe(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(-3)));
        Codigo(() => FolhaDoMes.InicioDoMes("11/2026", DateTimeOffset.UtcNow)).ShouldBe("FOLHA_MES");
    }

    [Theory]
    [InlineData("123.456.789-09", "12345678909", true)]
    [InlineData("11987654321", "+5511987654321", true)]      // o Asaas pode devolver o celular sem o 55
    [InlineData("Terceiro.Pix@Exemplo.Invalid", "terceiro.pix@exemplo.invalid", true)]
    [InlineData("12345678900", "12345678909", false)]
    [InlineData("outro@exemplo.invalid", "terceiro.pix@exemplo.invalid", false)]
    [InlineData("0192f000-0000-7000-8000-0000000000ab", "0192f000-0000-7000-8000-0000000000ac", false)]
    public void Chave_de_destino_e_comparada_sem_pontuacao(string recebida, string aprovada, bool igual) =>
        WebhooksDoAsaas.MesmaChave(recebida, aprovada).ShouldBe(igual);
}
