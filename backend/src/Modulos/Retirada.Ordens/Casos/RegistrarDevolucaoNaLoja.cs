using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Retirada.Comum;
using Retirada.Ordens.Dominio;

namespace Retirada.Ordens.Casos;

/// <summary>
/// RF15 e RN13: o cliente devolveu na loja; vale até depois de encerrada sem retirada.
/// Com todos os itens, a ordem encerra; com parte, segue aberta com o que falta recolher.
/// </summary>
internal static class RegistrarDevolucaoNaLoja
{
    /// <param name="Itens">Os itens que o cliente levou à loja. Sem a lista, todos os que ainda estão com ele.</param>
    internal sealed record PedidoDeDevolucao(IReadOnlyList<Guid>? Itens);

    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapPost("/{id:guid}/devolucao-na-loja", ([AsParameters] PedidoDeComando p, PedidoDeDevolucao? corpo) =>
                ComandoDaOrdem.ExecutarAsync(p, (ordem, agora) =>
                {
                    var itens = corpo?.Itens ??
                        [.. ordem.Equipamentos.Where(e => e.Situacao == SituacaoEquipamento.Pendente).Select(e => e.Id)];
                    var encerrou = ordem.RegistrarDevolucaoNaLoja(itens, agora);
                    return Task.FromResult<object?>(new { itens, encerrou });
                }))
            .ComoComando(Politicas.Estoque, "Registrar devolução na loja, de todos ou de parte dos itens (RF15)");
}
