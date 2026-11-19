using System.IO.Compression;
using System.Text;
using Retirada.Comum;
using Retirada.Ordens.Dominio;
using Retirada.Ordens.Importacao;
using Shouldly;

namespace Retirada.Ordens.Testes;

/// <summary>Importação (RF03–RF05, RN01–RN06, RN15, RN16) sem banco: leitura, colunas, validação e prévia.</summary>
public class ImportacaoTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private static byte[] Planilha(string nome) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Planilhas", nome));

    private static Previa Prever(byte[] conteudo, string nome, RetratoDoBanco? banco = null, bool simulacao = true)
    {
        var arquivo = LeitorDeArquivo.Ler(conteudo, nome);
        var colunas = Colunas.Mapear(arquivo.Cabecalhos);
        var linhas = arquivo.Linhas.Select(l => Interpretador.Interpretar(l, colunas, simulacao)).ToList();
        return MontadorDaPrevia.Montar(arquivo, colunas, linhas, banco ?? RetratoDoBanco.Vazio);
    }

    private static string CsvDoHubsoft(params string[] linhas) =>
        "nome_razaosocial;data_inicio_programado;hora_inicio_programado;tipo_ordem_servico;endereco;numero;bairro;cidade;estado;telefone_primario;id_ordem_servico;equipamento_tipo;equipamento_modelo;numero_serie\n"
        + string.Join('\n', linhas);

    private static void DeveRecusar(Action acao, string codigo, string trecho)
    {
        var erro = Should.Throw<RegraDeNegocioException>(acao);
        erro.Codigo.ShouldBe(codigo);
        erro.Message.ShouldContain(trecho);
    }

    // ------------------------------------------------------------------ planilha fictícia do financeiro

    [Fact]
    public void Planilha_do_financeiro_vira_previa_com_duplicada_e_pendencias() // C01, C19 e C20
    {
        var previa = Prever(Planilha("planilha_retiradas_ficticia.xlsx"), "retiradas.xlsx");

        previa.Formato.ShouldBe(FormatoDoArquivo.Xlsx);
        previa.Linhas.Count.ShouldBe(61);
        previa.Contar(ResultadoDaLinha.Duplicada).ShouldBe(1);  // C01: protocolo repetido
        previa.Contar(ResultadoDaLinha.Pendencia).ShouldBe(5);  // C19: serviço suspenso, sem rescisão
        previa.Contar(ResultadoDaLinha.Erro).ShouldBe(0);
        previa.Novas.Count.ShouldBe(55);
        previa.Novas.ShouldAllBe(g => g.Dados.Chave.StartsWith("protocolo:"));
    }

    [Fact]
    public void Colunas_com_dado_pessoal_sao_descartadas_e_nunca_lidas() // RN04, C21 e C22
    {
        var previa = Prever(Planilha("planilha_retiradas_ficticia.xlsx"), "retiradas.xlsx");

        previa.Colunas.Descartadas.ShouldBe(
            ["Data de abertura", "Plano", "Dias em atraso", "CPF/CNPJ", "E-mail", "Nº do contrato", "Data de ativação", "Valor em aberto (R$)", "Login PPPoE"],
            ignoreOrder: true);
        // Nenhum CPF chega à prévia, nem como detalhe (todos têm dígito errado de propósito e não derrubam a linha).
        previa.Linhas.SelectMany(l => l.Detalhes).ShouldNotContain(d => d.Contains("CPF") || d.Contains(".invalid"));
    }

    [Fact]
    public void Telefones_viram_E164_e_o_incompleto_so_gera_alerta() // RN05 e C04
    {
        var previa = Prever(Planilha("planilha_retiradas_ficticia.xlsx"), "retiradas.xlsx");

        var incompleto = previa.Novas.Single(g => g.Dados.Referencias.Protocolo == "20260917150152621018");
        incompleto.Telefones.ShouldContain(t => !t.FormatoValido);
        previa.Linhas.Single(l => l.Numero == incompleto.PrimeiraLinha).Detalhes.ShouldContain(d => d.Contains("fora do padrão"));
        previa.Novas.SelectMany(g => g.Telefones).Where(t => t.FormatoValido).ShouldAllBe(t => t.NumeroE164.StartsWith("+5500"));
    }

    [Fact]
    public void Texto_que_parece_formula_entra_como_texto() // RN06 e C06
    {
        var previa = Prever(Planilha("planilha_retiradas_ficticia.xlsx"), "retiradas.xlsx");

        previa.Novas.Single(g => g.Dados.Referencias.Protocolo == "20260917083612923637").Dados.Observacao.ShouldStartWith("=");
    }

    // ------------------------------------------------------------------ exportação do HubSoft

    [Fact]
    public void Exportacao_do_HubSoft_junta_os_equipamentos_da_mesma_OS() // uma linha por equipamento
    {
        var previa = Prever(Planilha("modelo_exportacao_financeiro.xlsx"), "exportacao.xlsx");

        var primeira = previa.Novas.Single(g => g.Dados.Chave == "os:9101");
        primeira.Equipamentos.Select(e => e.Tipo).ShouldBe([TipoEquipamento.ONU, TipoEquipamento.Roteador]);
        primeira.Equipamentos[0].Serie!.Valor.ShouldBe("HWTC1A2B3C4D");
        primeira.Equipamentos[0].Mac.ShouldBe("02:1A:2B:3C:4D:5E");
        primeira.Dados.Referencias.ShouldSatisfyAllConditions(
            r => r.Protocolo.ShouldBe("20260914101500123456"),
            r => r.DataProgramada.ShouldBe(new DateTimeOffset(2026, 9, 15, 9, 0, 0, TimeSpan.FromHours(-3))),
            r => r.DataRescisao.ShouldBe(new DateOnly(2026, 9, 14)));
        primeira.Dados.Cep.ShouldBe("00000-050");
        previa.Novas.Single(g => g.Dados.Chave == "os:9102").Telefones.Single().Tipo.ShouldBe("fixo");
    }

    // ------------------------------------------------------------------ formatos (RN15)

    [Fact]
    public void CSV_do_bloco_de_notas_com_acento_e_ponto_e_virgula()
    {
        var csv = CsvDoHubsoft("JOSÉ EXEMPLO;15/09/2026;09:00:00;REMOÇÃO EQUIP FTTH;RUA DOS IPÊS;120;SÃO JOSÉ;CIDADE SIMULADA;MG;00912345678;9201;ONU;Modelo X;SIMU0000AAAA");
        var previa = Prever(Encoding.GetEncoding(1252).GetBytes(csv), "ordens.csv");

        previa.Formato.ShouldBe(FormatoDoArquivo.Csv);
        previa.Novas.Single().Dados.ShouldSatisfyAllConditions(
            d => d.Cliente.ShouldBe("JOSÉ EXEMPLO"),
            d => d.Endereco.Bairro.ShouldBe("SÃO JOSÉ"));
    }

    [Fact]
    public void CSV_com_virgula_e_campo_entre_aspas()
    {
        var csv = "protocolo,nome_razaosocial,tipo_ordem_servico,endereco,numero,bairro,cidade,estado,telefone_primario,observacao\n"
            + "20260914101500123456,\"SILVA, MARIA\",REMOÇÃO EQUIP FTTH,RUA A,1,CENTRO,CIDADE SIMULADA,MG,00912345678,\"disse \"\"sim\"\"\"";
        var previa = Prever(Encoding.UTF8.GetBytes(csv), "ordens.csv");

        previa.Novas.Single().Dados.ShouldSatisfyAllConditions(
            d => d.Cliente.ShouldBe("SILVA, MARIA"),
            d => d.Observacao.ShouldBe("disse \"sim\""));
    }

    [Fact]
    public void JSON_e_XML_viram_a_mesma_ordem_que_a_planilha()
    {
        const string Json = """
            { "ordens": [ { "nome_razaosocial": "ANA EXEMPLO", "tipo_ordem_servico": "REMOÇÃO EQUIP FTTH", "endereco": "RUA B", "numero": "10",
                            "bairro": "CENTRO", "cidade": "CIDADE SIMULADA", "estado": "MG", "telefone_primario": "00912345678",
                            "id_ordem_servico": 9301, "equipamento_tipo": "Fonte", "equipamento_modelo": "Fonte 12V" } ] }
            """;
        const string Xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <ordens><ordem><nome_razaosocial>ANA EXEMPLO</nome_razaosocial><tipo_ordem_servico>REMOÇÃO EQUIP FTTH</tipo_ordem_servico>
            <endereco>RUA B</endereco><numero>10</numero><bairro>CENTRO</bairro><cidade>CIDADE SIMULADA</cidade><estado>MG</estado>
            <telefone_primario>00912345678</telefone_primario><id_ordem_servico>9301</id_ordem_servico>
            <equipamento_tipo>Fonte</equipamento_tipo><equipamento_modelo>Fonte 12V</equipamento_modelo></ordem></ordens>
            """;

        var doJson = Prever(Encoding.UTF8.GetBytes(Json), "ordens.json").Novas.Single();
        var doXml = Prever(Encoding.UTF8.GetBytes(Xml), "ordens.xml").Novas.Single();

        doJson.Dados.Chave.ShouldBe("os:9301");
        doXml.Dados.DadosDaOs.ShouldBe(doJson.Dados.DadosDaOs);
        doXml.Equipamentos.Single().ShouldBe(doJson.Equipamentos.Single()); // fonte, sem série
    }

    [Fact]
    public void ODS_do_LibreOffice_e_lido_como_planilha()
    {
        var previa = Prever(Ods(
            ["protocolo", "nome_razaosocial", "tipo_ordem_servico", "endereco", "numero", "bairro", "cidade", "estado", "telefone_primario"],
            ["20260914101500123456", "BETO EXEMPLO", "REMOÇÃO EQUIP FTTH", "RUA C", "5", "CENTRO", "CIDADE SIMULADA", "MG", "00912345678"]), "ordens.ods");

        previa.Formato.ShouldBe(FormatoDoArquivo.Ods);
        previa.Novas.Single().Dados.Cliente.ShouldBe("BETO EXEMPLO");
    }

    [Fact]
    public void O_formato_vem_do_conteudo_e_nao_da_extensao()
    {
        var csv = CsvDoHubsoft("ANA;15/09/2026;09:00:00;REMOÇÃO EQUIP FTTH;RUA;1;CENTRO;CIDADE;MG;00912345678;9401;;;");
        Prever(Encoding.UTF8.GetBytes(csv), "planilha.xlsx").Formato.ShouldBe(FormatoDoArquivo.Csv);
    }

    [Fact] public void Arquivo_sql_e_recusado() => DeveRecusar(() => LeitorDeArquivo.Ler("SELECT 1"u8.ToArray(), "backup.sql"), "RN15", "exporte a consulta");

    [Fact] public void PDF_e_recusado() => DeveRecusar(() => LeitorDeArquivo.Ler("%PDF-1.7 ..."u8.ToArray(), "ordens.txt"), "RN15", "Formato não aceito");

    [Fact] public void Arquivo_vazio_e_recusado() => DeveRecusar(() => LeitorDeArquivo.Ler([], "ordens.csv"), "RN15", "vazio");

    [Fact]
    public void XML_com_DTD_e_recusado_para_nao_ler_arquivo_do_servidor() // RN16 (XXE)
    {
        const string Xxe = """<?xml version="1.0"?><!DOCTYPE x [<!ENTITY e SYSTEM "file:///etc/passwd">]><ordens><ordem><nome>&e;</nome></ordem></ordens>""";
        DeveRecusar(() => LeitorDeArquivo.Ler(Encoding.UTF8.GetBytes(Xxe), "ordens.xml"), "RN15", "DTD");
    }

    [Fact]
    public void Zip_que_nao_e_planilha_e_recusado()
    {
        using var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, true))
            using (var escrita = new StreamWriter(zip.CreateEntry("leia.txt").Open())) escrita.Write("oi");
        DeveRecusar(() => LeitorDeArquivo.Ler(memoria.ToArray(), "ordens.xlsx"), "RN15", "não é uma planilha");
    }

    // ------------------------------------------------------------------ colunas e linhas

    [Fact]
    public void Coluna_obrigatoria_faltando_recusa_o_arquivo_com_o_nome_dela() // RF04 revisado em nov/2026
    {
        // Só cliente, endereço e cidade são obrigatórios. Faltar o resto vira aviso e padrão.
        var semCliente = "protocolo;tipo_ordem_servico;endereco;numero;bairro;cidade;estado;telefone_primario\n"
            + "20260914101500123456;REMOÇÃO EQUIP FTTH;RUA;1;CENTRO;CIDADE;MG;00912345678";
        DeveRecusar(() => Prever(Encoding.UTF8.GetBytes(semCliente), "ordens.csv"), "RF04", "Falta a coluna cliente");
    }

    [Fact]
    public void Planilha_so_com_cliente_endereco_e_cidade_entra_com_padroes_e_avisos()
    {
        // Pouca coluna, qualquer sinônimo: cliente+endereço+cidade bastam.
        var csv = "nome;rua;cidade\n" + "ANA EXEMPLO;RUA DOS IPÊS;CIDADE SIMULADA";
        var previa = Prever(Encoding.UTF8.GetBytes(csv), "basica.csv");

        previa.Novas.Count.ShouldBe(1);
        var ordem = previa.Novas[0].Dados;
        ordem.Cliente.ShouldBe("ANA EXEMPLO");
        ordem.Endereco.Numero.ShouldBe("s/n");
        ordem.Endereco.Bairro.ShouldBe("(não informado)");
        ordem.Endereco.Uf.ShouldBe("");
        ordem.TipoOs.ShouldBe("RETIRADA DE EQUIPAMENTO");
        previa.AvisosDoArquivo.ShouldContain(a => a.Contains("tipo de OS"));
        previa.AvisosDoArquivo.ShouldContain(a => a.Contains("número"));
        previa.AvisosDoArquivo.ShouldContain(a => a.Contains("UF"));
        previa.AvisosDoArquivo.ShouldContain(a => a.Contains("telefone"));
    }

    [Fact]
    public void Sem_id_nem_protocolo_a_chave_provisoria_e_a_mesma_ao_reenviar() // RN01
    {
        var csv = "nome_razaosocial;data_inicio_programado;hora_inicio_programado;tipo_ordem_servico;endereco;numero;bairro;cidade;estado;telefone_primario\n"
            + "ANA EXEMPLO;15/09/2026;09:00:00;REMOÇÃO EQUIP FTTH;RUA DOS IPÊS;120;CENTRO;CIDADE SIMULADA;MG;00912345678";

        var primeira = Prever(Encoding.UTF8.GetBytes(csv), "a.csv");
        var segunda = Prever(Encoding.UTF8.GetBytes(csv.Replace("ANA EXEMPLO", "ana  exemplo")), "b.csv");

        primeira.Novas.Single().Dados.Chave.ShouldStartWith("provisoria:");
        segunda.Novas.Single().Dados.Chave.ShouldBe(primeira.Novas.Single().Dados.Chave);
        primeira.AvisosDoArquivo.ShouldContain(a => a.Contains("chave é provisória"));
    }

    [Fact]
    public void Linha_com_erro_fica_fora_e_o_resto_entra() // UC01
    {
        var previa = Prever(Encoding.UTF8.GetBytes(CsvDoHubsoft(
            "ANA;15/09/2026;09:00:00;REMOÇÃO EQUIP FTTH;RUA;1;CENTRO;CIDADE;MG;00912345678;9501;ONU;Modelo;SIMU0000BBBB",
            "BIA;15/09/2026;09:00:00;REMOÇÃO EQUIP FTTH;RUA;2;;CIDADE;MG;00912345678;9502;ONU;Modelo;SIMU0000CCCC",
            "CAIO;15/09/2026;09:00:00;TROCA DE PLANO;RUA;3;CENTRO;CIDADE;MG;00912345678;9503;ONU;Modelo;SIMU0000DDDD",
            "DUDA;15/09/2026;09:00:00;REMOÇÃO EQUIP FTTH;RUA;4;CENTRO;CIDADE;MG;00912345678;9504;ONU;Modelo;")), "ordens.csv");

        // Linha 2 (BIA) entra também: bairro vazio virou aviso, não erro (RF04 revisado).
        // Linha 3 (CAIO) é IGNORADA silenciosamente: a planilha do HubSoft vem com todos os tipos (REMOÇÃO,
        // REATIVAÇÃO, TROCA DE PLANO…) misturados; o que não é de remoção não é erro, só não interessa.
        previa.Novas.Select(g => g.Dados.Chave).OrderBy(c => c).ShouldBe(["os:9501", "os:9502"]);
        previa.Linhas.Single(l => l.Numero == 3).Detalhes.ShouldContain(d => d.Contains("sem bairro"));
        var ignorada = previa.Linhas.Single(l => l.Numero == 4);
        ignorada.Resultado.ShouldBe(ResultadoDaLinha.Ignorada);
        ignorada.Detalhes.ShouldContain(d => d.Contains("TROCA DE PLANO"));
        previa.Linhas.Single(l => l.Numero == 5).Detalhes.ShouldContain(d => d.StartsWith("falta o número de série"));
    }

    [Fact]
    public void Plano_e_observacao_saem_do_texto_livre_sem_arrastar_dado_sensivel() // RF03 + RN04
    {
        // Simula a descrição real do HubSoft, que vem multilinha dentro da célula (RFC 4180 permite \n entre aspas).
        var csv = "nome_razaosocial;data_inicio_programado;hora_inicio_programado;tipo_ordem_servico;endereco;numero;bairro;cidade;estado;id_ordem_servico;descricao_servico;equipamento_tipo;equipamento_modelo;numero_serie\n"
            + "ANA;15/09/2026;09:00:00;REMOÇÃO EQUIP FTTH;RUA;1;CENTRO;CIDADE;MG;9001;"
            + "\"SOLICITANTE: MARIA\nTELEFONE PARA CONTATO: (00) 99123-4567\nPLANO: 600 MB 99,99\n"
            + "DÉBITOS: 2 mensalidades\nDISPONIBILIDADE PARA ATENDIMENTO: cliente pediu no sábado\n"
            + "CLIENTE EM CASA: ligar antes de ir\nMOTIVO DO CANCELAMENTO: trocou de provedor\""
            + ";ONU;Modelo;SIMU0000IIII";

        var previa = Prever(Encoding.UTF8.GetBytes(csv), "ordens.csv");
        var grupo = previa.Novas.Single();

        grupo.Dados.Plano.ShouldBe("600 Mbps");
        grupo.Dados.Observacao.ShouldNotBeNull();
        grupo.Dados.Observacao!.ShouldContain("DISPONIBILIDADE PARA ATENDIMENTO: cliente pediu no sábado");
        grupo.Dados.Observacao.ShouldContain("CLIENTE EM CASA: ligar antes de ir");
        // RN04: campos sensíveis NÃO entram na observação (allow-list de rótulos seguros).
        grupo.Dados.Observacao.ShouldNotContain("MARIA");
        grupo.Dados.Observacao.ShouldNotContain("DÉBITOS");
        grupo.Dados.Observacao.ShouldNotContain("MOTIVO");
        grupo.Dados.Observacao.ShouldNotContain("TELEFONE");
        // Telefone é extraído do texto livre pelo caminho já existente (RN05 em simulação).
        grupo.Telefones.Count.ShouldBe(1);
    }

    [Fact]
    public void Aviso_destaca_quantas_linhas_de_cada_tipo_foram_ignoradas()
    {
        var previa = Prever(Encoding.UTF8.GetBytes(CsvDoHubsoft(
            "ANA;15/09/2026;09:00:00;REMOÇÃO EQUIP FTTH;RUA;1;CENTRO;CIDADE;MG;00912345678;6001;ONU;Modelo;SIMU00006001",
            "BIA;15/09/2026;09:00:00;REATIVAÇÃO;RUA;2;CENTRO;CIDADE;MG;00912345678;6002;ONU;Modelo;SIMU00006002",
            "CAIO;15/09/2026;09:00:00;REATIVAÇÃO;RUA;3;CENTRO;CIDADE;MG;00912345678;6003;ONU;Modelo;SIMU00006003",
            "DUDA;15/09/2026;09:00:00;INSTALAÇÃO FTTH;RUA;4;CENTRO;CIDADE;MG;00912345678;6004;ONU;Modelo;SIMU00006004")),
            "ordens.csv");

        previa.AvisosDoArquivo.ShouldContain(a => a.Contains("3 linhas") && a.Contains("REATIVAÇÃO") && a.Contains("INSTALAÇÃO FTTH"));
    }

    [Fact]
    public void HubSoft_sem_coluna_de_telefone_extrai_do_texto_livre_e_descarta_o_resto() // RN04 + HubSoft real
    {
        // No HubSoft real a coluna `telefones` costuma vir vazia e o contato fica dentro de `descricao_servico`
        // (que também carrega dado pessoal). A importação precisa extrair SÓ o telefone e esquecer o resto.
        var csv = "nome_razaosocial;data_inicio_programado;hora_inicio_programado;tipo_ordem_servico;endereco;numero;bairro;cidade;estado;id_ordem_servico;descricao_servico;equipamento_tipo;equipamento_modelo;numero_serie\n"
            + "ANA;15/09/2026;09:00:00;REMOÇÃO EQUIP FTTH;RUA;1;CENTRO;CIDADE;MG;8100;TELEFONE PARA CONTATO: (00) 99123-4567 PLANO: 600 MB DEBITOS: 2 mensalidades;ONU;Modelo;SIMU0000FFFF";
        var previa = Prever(Encoding.UTF8.GetBytes(csv), "ordens.csv");

        var grupo = previa.Novas.Single();
        grupo.Telefones.Count.ShouldBe(1);
        grupo.Telefones[0].NumeroE164.ShouldBe("+5500991234567");
        grupo.Telefones[0].FormatoValido.ShouldBeTrue();
        // RN04: nenhum trecho da descrição vira observação ou outro campo.
        grupo.Dados.Observacao.ShouldBeNull();
    }

    [Fact]
    public void HubSoft_com_tipos_misturados_pula_os_que_nao_sao_de_remocao_sem_erro()
    {
        var previa = Prever(Encoding.UTF8.GetBytes(CsvDoHubsoft(
            "ANA;15/09/2026;09:00:00;REMOÇÃO EQUIP FTTH;RUA;1;CENTRO;CIDADE;MG;00912345678;7001;ONU;Modelo;SIMU0000AAAA",
            "BIA;15/09/2026;09:00:00;REATIVAÇÃO;RUA;2;CENTRO;CIDADE;MG;00912345678;7002;ONU;Modelo;SIMU0000BBBB",
            "CAIO;15/09/2026;09:00:00;INSTALAÇÃO FTTH;RUA;3;CENTRO;CIDADE;MG;00912345678;7003;ONU;Modelo;SIMU0000CCCC",
            "DUDA;15/09/2026;09:00:00;MANUTENÇÃO PREVENTIVA;RUA;4;CENTRO;CIDADE;MG;00912345678;7004;ONU;Modelo;SIMU0000DDDD")),
            "ordens.csv");

        previa.Novas.Select(g => g.Dados.Chave).ShouldBe(["os:7001"]);
        previa.Linhas.Where(l => l.Resultado == ResultadoDaLinha.Ignorada).Select(l => l.Numero).ShouldBe([3, 4, 5]);
        previa.Linhas.Count(l => l.Resultado == ResultadoDaLinha.Erro).ShouldBe(0);
    }

    [Fact]
    public void Bloqueio_temporario_vira_pendencia_e_nao_ordem() // RN02
    {
        var previa = Prever(Encoding.UTF8.GetBytes(CsvDoHubsoft(
            "ANA;15/09/2026;09:00:00;REMOÇÃO EQUIP BLOQUEIO TEMPORARIO;RUA;1;CENTRO;CIDADE;MG;00912345678;9601;ONU;Modelo;SIMU0000EEEE")), "ordens.csv");

        previa.Novas.ShouldBeEmpty();
        previa.Pendencias.Single().Dados.Pendencia.ShouldBe(MotivoDePendencia.BloqueioTemporario);
    }

    // ------------------------------------------------------------------ contra o que já está no banco

    private static Ordem Gravada(string chave, Endereco endereco, string serie, StatusOrdem? levarPara = null)
    {
        // Gravada a partir da mesma linha: a data programada é a da planilha (15/09/2026 09:00 em Brasília).
        var ordem = Ordem.Importar(chave, null, "REMOÇÃO EQUIP FTTH", Guid.NewGuid(), endereco, null, null,
            [Equipamento.Criar(TipoEquipamento.ONU, "Modelo", NumeroSerie.Criar(serie))], Agora, TimeSpan.FromHours(48),
            new ReferenciasDaImportacao(IdOrdemServico: 9701, DataProgramada: new DateTimeOffset(2026, 9, 15, 9, 0, 0, TimeSpan.FromHours(-3))));
        if (levarPara == StatusOrdem.Cancelada) ordem.Cancelar(MotivoCancelamento.Pagou, Agora);
        return ordem;
    }

    private static RetratoDoBanco Com(params Ordem[] ordens) => RetratoDoBanco.Vazio with
    {
        OrdensPorChave = ordens.ToDictionary(o => o.ChaveImportacao),
        ChavePorSerieEmAberto = ordens.SelectMany(o => o.Equipamentos.Select(e => (e.NumeroSerie!.Valor, o.ChaveImportacao))).ToDictionary(x => x.Item1, x => x.Item2),
    };

    private static readonly string LinhaDaAna = "ANA;15/09/2026;09:00:00;REMOÇÃO EQUIP FTTH;RUA;1;CENTRO;CIDADE;MG;00912345678;9701;ONU;Modelo;SIMU0000FFFF";

    [Fact]
    public void Reimportar_sem_mudanca_nao_duplica() // RN01 e C02
    {
        var banco = Com(Gravada("os:9701", new Endereco("RUA", "1", null, "CENTRO", "CIDADE", "MG", null), "SIMU0000FFFF"));

        var previa = Prever(Encoding.UTF8.GetBytes(CsvDoHubsoft(LinhaDaAna)), "ordens.csv", banco);

        previa.Novas.ShouldBeEmpty();
        previa.Atualizacoes.ShouldBeEmpty();
        previa.Linhas.Single().ShouldSatisfyAllConditions(
            l => l.Resultado.ShouldBe(ResultadoDaLinha.Repetida),
            l => l.Detalhes.ShouldContain("já importada, sem mudança"));
    }

    [Fact]
    public void Reimportar_com_endereco_novo_atualiza_so_o_que_mudou() // RN01 e C02
    {
        var banco = Com(Gravada("os:9701", new Endereco("RUA ANTIGA", "1", null, "CENTRO", "CIDADE", "MG", null), "SIMU0000FFFF"));

        var previa = Prever(Encoding.UTF8.GetBytes(CsvDoHubsoft(LinhaDaAna)), "ordens.csv", banco);

        previa.Atualizacoes.Single().Mudancas.ShouldBe(["endereço"]);
        previa.Linhas.Single().Resultado.ShouldBe(ResultadoDaLinha.Atualizacao);
    }

    [Fact]
    public void Ordem_encerrada_nao_e_atualizada_pela_planilha()
    {
        var banco = Com(Gravada("os:9701", new Endereco("RUA ANTIGA", "1", null, "CENTRO", "CIDADE", "MG", null), "SIMU0000FFFF", StatusOrdem.Cancelada));

        var previa = Prever(Encoding.UTF8.GetBytes(CsvDoHubsoft(LinhaDaAna)), "ordens.csv", banco);

        previa.Atualizacoes.ShouldBeEmpty();
        previa.Linhas.Single().Detalhes.ShouldContain("ordem já encerrada: fica como está");
    }

    [Fact]
    public void Serie_em_aberto_em_outra_ordem_barra_a_OS_nova() // RN11
    {
        var banco = Com(Gravada("os:1", new Endereco("RUA", "1", null, "CENTRO", "CIDADE", "MG", null), "SIMU0000FFFF"));

        var previa = Prever(Encoding.UTF8.GetBytes(CsvDoHubsoft(LinhaDaAna)), "ordens.csv", banco);

        previa.Novas.ShouldBeEmpty();
        previa.Linhas.Single().Detalhes.ShouldContain(d => d.Contains("RN11"));
    }

    // ------------------------------------------------------------------ telefones (RN05)

    [Theory]
    [InlineData("(37) 99123-4567", false, true, "celular", "+5537991234567")]
    [InlineData("3732214567", false, true, "fixo", "+553732214567")]
    [InlineData("+55 37 99123-4567", false, true, "celular", "+5537991234567")]
    [InlineData("(00) 91094-7780", false, false, "desconhecido", "+5500910947780")]
    [InlineData("(00) 91094-7780", true, true, "celular", "+5500910947780")]
    [InlineData("(00) 9123", true, false, "desconhecido", "+55009123")]
    public void Telefone_padronizado(string comoVeio, bool simulacao, bool valido, string tipo, string e164)
    {
        var telefone = Interpretador.Telefone(comoVeio, simulacao)!;
        telefone.ShouldSatisfyAllConditions(
            t => t.FormatoValido.ShouldBe(valido),
            t => t.Tipo.ShouldBe(tipo),
            t => t.NumeroE164.ShouldBe(e164),
            t => t.ComoVeio.ShouldBe(comoVeio));
    }

    [Theory]
    [InlineData("Nº de série", "no_de_serie")]
    [InlineData("Código do cliente", "codigo_do_cliente")]
    [InlineData("  Telefone 1 ", "telefone_1")]
    [InlineData("Valor em aberto (R$)", "valor_em_aberto_r")]
    public void Cabecalho_normalizado(string cabecalho, string esperado) => Colunas.Normalizar(cabecalho).ShouldBe(esperado);

    /// <summary>Monta um .ods mínimo (o que o LibreOffice grava) para o teste não depender de arquivo pronto.</summary>
    private static byte[] Ods(params string[][] linhas)
    {
        var celulas = string.Join("", linhas.Select(l =>
            "<table:table-row>" + string.Join("", l.Select(c => $"<table:table-cell office:value-type=\"string\"><text:p>{c}</text:p></table:table-cell>"))
            + "<table:table-cell table:number-columns-repeated=\"1000\"/></table:table-row>"));
        var conteudo = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
              xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0" xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0">
              <office:body><office:spreadsheet><table:table table:name="Ordens">{celulas}
              <table:table-row table:number-rows-repeated="1048000"><table:table-cell table:number-columns-repeated="1024"/></table:table-row>
              </table:table></office:spreadsheet></office:body></office:document-content>
            """;
        using var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, true))
        {
            using (var m = new StreamWriter(zip.CreateEntry("mimetype", CompressionLevel.NoCompression).Open())) m.Write("application/vnd.oasis.opendocument.spreadsheet");
            using (var c = new StreamWriter(zip.CreateEntry("content.xml").Open())) c.Write(conteudo);
        }
        return memoria.ToArray();
    }
}
