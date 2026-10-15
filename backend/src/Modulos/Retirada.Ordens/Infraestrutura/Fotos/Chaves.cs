namespace Retirada.Ordens.Infraestrutura.Fotos;

/// <summary>
/// Onde cada foto mora no armazenamento. A enviada fica em "pendentes/" até a retirada ser registrada;
/// aí é copiada para "ordens/", que é o caminho gravado em evidencia.arquivo_chave.
/// </summary>
internal static class Chaves
{
    public const string PrefixoPendente = "pendentes/";

    public static string Pendente(Guid ordemId, Guid fotoId) => $"{PrefixoPendente}{ordemId:N}/{fotoId:N}.jpg";

    public static string DaOrdem(Guid ordemId, Guid fotoId) => $"ordens/{ordemId:N}/{fotoId:N}.jpg";
}
