using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Retirada.Comum;
using Retirada.Ordens.Dominio;
using Retirada.Ordens.Infraestrutura;

namespace Retirada.Ordens.Casos;

/// <summary>
/// RN10: registra uma tentativa de contato (ligação ou WhatsApp) e o resultado. Grava a tentativa
/// (só acréscimo), o evento no histórico e o que o resultado diz do telefone, tudo na mesma transação.
/// O sistema não liga nem manda mensagem: quem contata é a pessoa, pelo celular e pelo WhatsApp oficial.
/// </summary>
internal static class RegistrarContato
{
    internal sealed record PedidoDeContato(Guid TelefoneId, CanalContato Canal, ResultadoContato Resultado, ModeloDeMensagem? Modelo);

    internal sealed record TelefoneDoCliente(bool FormatoValido);

    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapPost("/{id:guid}/contatos", ([AsParameters] PedidoDeComando p, PedidoDeContato corpo) =>
            {
                var foraDaJanela = false;
                return ComandoDaOrdem.ExecutarAsync(p,
                    async (ordem, agora) =>
                    {
                        var telefone = await p.Db.Database.SqlQuery<TelefoneDoCliente>($"""
                            SELECT formato_valido AS "FormatoValido" FROM retirada.telefone_cliente
                             WHERE id = {corpo.TelefoneId} AND cliente_id = {ordem.ClienteId}
                            """).SingleOrDefaultAsync(p.Ct)
                            ?? throw new RegraDeNegocioException("RN10", "Esse telefone não é do cliente desta ordem.");
                        if (!telefone.FormatoValido)
                            throw new RegraDeNegocioException("RN05", "Número fora do padrão: corrija o cadastro antes de contatar.");

                        ordem.RegistrarContato(corpo.Canal, corpo.Resultado, corpo.Modelo, agora);
                        foraDaJanela = !JanelaDeContato.Permite(agora);
                        // Só o id do telefone vai para o histórico; o número fica no cadastro do cliente.
                        return new { canal = corpo.Canal, telefoneId = corpo.TelefoneId, modelo = corpo.Modelo, foraDaJanela };
                    },
                    gravarJunto: (ordem, agora) => GravarAsync(p, ordem, corpo, foraDaJanela, agora));
            })
            .ComoComando(Politicas.Contato, "Registrar tentativa de contato com o cliente (RN10)");

    private static async Task GravarAsync(PedidoDeComando p, Ordem ordem, PedidoDeContato corpo, bool foraDaJanela, DateTimeOffset agora)
    {
        var canal = OrdensDbContext.ParaSnake(corpo.Canal.ToString());
        var resultado = OrdensDbContext.ParaSnake(corpo.Resultado.ToString());
        var modelo = corpo.Modelo is { } m ? OrdensDbContext.ParaSnake(m.ToString()) : null;
        var (linha, whatsapp) = OQueDizDoTelefone(corpo.Resultado);

        await p.Db.Database.ExecuteSqlAsync($"""
            INSERT INTO retirada.tentativa_contato (ordem_id, telefone_id, canal, modelo, resultado, feito_por, feito_em, fora_da_janela)
            VALUES ({ordem.Id}, {corpo.TelefoneId}, {canal}, {modelo}, {resultado}, {p.Usuario.Id()}, {agora}, {foraDaJanela});

            UPDATE retirada.telefone_cliente
               SET linha = COALESCE({linha}, linha), whatsapp = COALESCE({whatsapp}, whatsapp), verificado_em = {agora}
             WHERE id = {corpo.TelefoneId};
            """, p.Ct);
    }

    /// <summary>
    /// O resultado também conta como está o número. "Não atendeu" não prova nada; caixa postal prova que a linha existe.
    /// </summary>
    internal static (string? Linha, string? Whatsapp) OQueDizDoTelefone(ResultadoContato resultado) => resultado switch
    {
        ResultadoContato.Atendeu or ResultadoContato.CaixaPostal => ("atende", null),
        ResultadoContato.NumeroInexistente => ("inexistente", null),
        ResultadoContato.Enviada or ResultadoContato.Respondeu => (null, "tem"),
        ResultadoContato.SemWhatsapp => (null, "nao_tem"),
        ResultadoContato.NaoEntregue => (null, "nao_entregue"),
        _ => (null, null),
    };
}
