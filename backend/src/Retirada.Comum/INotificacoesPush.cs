namespace Retirada.Comum;

/// <summary>
/// Porta de saída para o envio de notificações push. O módulo de ordens usa esta interface
/// sem precisar referenciar o módulo de acesso, que contém a implementação concreta.
/// </summary>
public interface INotificacoesPush
{
    /// <summary>
    /// Envia uma notificação para todos os dispositivos inscritos do usuário.
    /// Não lança exceção se o push não estiver configurado — apenas ignora silenciosamente.
    /// </summary>
    Task EnviarAsync(Guid usuarioId, string titulo, string corpo, string? url, CancellationToken ct);
}
