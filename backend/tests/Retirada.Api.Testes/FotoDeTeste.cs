using Retirada.Ordens.Dominio;

namespace Retirada.Api.Testes;

/// <summary>RN14: a foto mínima da retirada, para os testes que não tratam das fotos.</summary>
internal static class FotoDeTeste
{
    public static readonly FotoDaRetirada[] DoConjunto = [new(TipoDeFoto.Equipamento)];
}
