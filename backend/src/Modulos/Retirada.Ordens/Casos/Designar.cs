using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Retirada.Comum;

namespace Retirada.Ordens.Casos;

/// <summary>RF08: o gestor designa a ordem Disponível a um técnico ativo, de qualquer bairro.</summary>
internal static class Designar
{
    internal sealed record PedidoDeDesignacao(Guid TecnicoId);

    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapPost("/{id:guid}/designar", ([AsParameters] PedidoDeComando p, PedidoDeDesignacao corpo) =>
                ComandoDaOrdem.ExecutarAsync(p, async (ordem, agora) =>
                {
                    var tecnicoAtivo = await p.Db.Usuarios.AsNoTracking().AnyAsync(
                        u => u.Id == corpo.TecnicoId && u.Papel == Papeis.Tecnico && u.Situacao == "ativo", p.Ct);
                    if (!tecnicoAtivo) throw new RegraDeNegocioException("RF08", "Escolha um técnico ativo.");

                    ordem.Designar(corpo.TecnicoId, agora);
                    return new { tecnicoId = corpo.TecnicoId };
                }))
            .ComoComando(Politicas.Gestao, "Designar a um técnico (RF08)");
}
