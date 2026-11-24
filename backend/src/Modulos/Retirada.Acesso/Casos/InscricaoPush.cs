using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Retirada.Acesso.Dominio;
using Retirada.Acesso.Infraestrutura;
using Retirada.Acesso.Seguranca;
using Retirada.Comum;

namespace Retirada.Acesso.Casos;

/// <summary>
/// Gerencia as inscrições de Web Push (VAPID) do usuário autenticado.
/// GET  /api/acesso/push/chave-publica   → devolve a chave pública VAPID (ou 204 se push não configurado)
/// POST /api/acesso/push/inscricao       → salva/atualiza a inscrição do dispositivo
/// DELETE /api/acesso/push/inscricao     → remove a inscrição do dispositivo atual
/// </summary>
internal static class InscricaoPush
{
    internal sealed record PedidoDeInscricao(
        string Endpoint,
        string ChaveP256Dh,
        string ChaveAuth,
        string? Agente);

    internal static void Mapear(IEndpointRouteBuilder grupo)
    {
        grupo.MapGet("/push/chave-publica", (ServicoDeNotificacoes svc) =>
        {
            var chave = svc.ChavePublicaVapid;
            return chave is null
                ? Results.NoContent()
                : Results.Ok(new { chavePublica = chave });
        })
        .AllowAnonymous()   // O frontend consulta antes de autenticar para saber se push está disponível.
        .WithSummary("Chave pública VAPID (RF — push de ordens)");

        grupo.MapPost("/push/inscricao", async (
            PedidoDeInscricao corpo,
            AcessoDbContext db,
            ClaimsPrincipal sessao,
            CancellationToken ct) =>
        {
            var usuarioId = sessao.Id();
            var agora = DateTimeOffset.UtcNow;

            var existente = await db.PushInscricoes
                .FirstOrDefaultAsync(p => p.Endpoint == corpo.Endpoint, ct);

            if (existente is null)
            {
                db.PushInscricoes.Add(PushInscricao.Criar(
                    usuarioId, corpo.Endpoint, corpo.ChaveP256Dh, corpo.ChaveAuth, corpo.Agente, agora));
            }
            else
            {
                // Mesmo endpoint: atualiza chaves (podem ter rotacionado) e transfere ao usuário atual.
                existente.AtualizarChaves(corpo.ChaveP256Dh, corpo.ChaveAuth, corpo.Agente, agora);
            }

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithSummary("Salvar inscrição de push do dispositivo");

        grupo.MapDelete("/push/inscricao", async (
            string endpoint,
            AcessoDbContext db,
            ClaimsPrincipal sessao,
            CancellationToken ct) =>
        {
            var usuarioId = sessao.Id();
            var inscricao = await db.PushInscricoes
                .FirstOrDefaultAsync(p => p.UsuarioId == usuarioId && p.Endpoint == endpoint, ct);

            if (inscricao is not null)
            {
                db.PushInscricoes.Remove(inscricao);
                await db.SaveChangesAsync(ct);
            }

            return Results.NoContent();
        })
        .WithSummary("Remover inscrição de push do dispositivo");
    }
}
