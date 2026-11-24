using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Retirada.Acesso.Dominio;
using Retirada.Acesso.Infraestrutura;
using Retirada.Comum;
using WebPush;

namespace Retirada.Acesso.Seguranca;

/// <summary>
/// Envia notificações Web Push (VAPID) para os dispositivos inscritos de um usuário.
/// Falhas de entrega individuais (endpoint expirado, dispositivo offline) são toleradas
/// e registradas; uma falha não cancela o envio para os outros dispositivos do mesmo usuário.
/// </summary>
public sealed class ServicoDeNotificacoes(
    IConfiguration config,
    AcessoDbContext db,
    ILogger<ServicoDeNotificacoes> logger) : INotificacoesPush
{
    private const string ChavePublicaCfg = "Push:VapidPublicKey";
    private const string ChavePrivadaCfg = "Push:VapidPrivateKey";
    private const string SubjectCfg = "Push:Subject";

    private bool Configurado =>
        !string.IsNullOrWhiteSpace(config[ChavePublicaCfg]) &&
        !string.IsNullOrWhiteSpace(config[ChavePrivadaCfg]);

    /// <summary>
    /// Devolve a chave pública VAPID para o frontend usar no <c>pushManager.subscribe()</c>.
    /// Devolve <c>null</c> se o push não estiver configurado.
    /// </summary>
    public string? ChavePublicaVapid => Configurado ? config[ChavePublicaCfg] : null;

    /// <summary>
    /// Envia uma notificação para todos os dispositivos inscritos do usuário.
    /// Não lança exceção se o push não estiver configurado — apenas ignora.
    /// </summary>
    public async Task EnviarAsync(Guid usuarioId, string titulo, string corpo, string? url, CancellationToken ct)
    {
        if (!Configurado) return;

        var inscricoes = db.PushInscricoes
            .Where(p => p.UsuarioId == usuarioId)
            .ToList();

        if (inscricoes.Count == 0) return;

        var payload = JsonSerializer.Serialize(new { titulo, corpo, url });
        var cliente = new WebPushClient();
        var vapid = new VapidDetails(
            subject: config[SubjectCfg] ?? "mailto:sem-resposta@exemplo.invalid",
            publicKey: config[ChavePublicaCfg]!,
            privateKey: config[ChavePrivadaCfg]!);

        var expirados = new List<PushInscricao>();

        foreach (var inscricao in inscricoes)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var assinatura = new PushSubscription(inscricao.Endpoint, inscricao.ChaveP256Dh, inscricao.ChaveAuth);
                await cliente.SendNotificationAsync(assinatura, payload, vapid);
            }
            catch (WebPushException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Gone or System.Net.HttpStatusCode.NotFound)
            {
                // Endpoint expirado: remove na próxima passagem fora do loop para não modificar a coleção.
                logger.LogInformation("Push: endpoint expirado para o usuário {UsuarioId}, removendo inscrição {EndpointTrecho}.",
                    usuarioId, inscricao.Endpoint[..Math.Min(60, inscricao.Endpoint.Length)]);
                expirados.Add(inscricao);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Push: falha ao enviar para o usuário {UsuarioId}.", usuarioId);
            }
        }

        if (expirados.Count > 0)
        {
            db.PushInscricoes.RemoveRange(expirados);
            await db.SaveChangesAsync(ct);
        }
    }
}
