using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Retirada.Comum;
using Retirada.Ordens.Dominio;

namespace Retirada.Ordens.Casos;

/// <summary>
/// Edição manual de uma ordem pelo financeiro ou gestor. Permite alterar todos os campos
/// antes do encerramento da ordem (Conferida, Cancelada, DevolvidaNaLoja, EncerradaSemRetirada).
/// Equipamentos já retirados ou devolvidos não podem ser removidos.
/// </summary>
internal static class EditarOrdem
{
    internal sealed record EnderecoEditado(
        string Logradouro, string? Numero, string? Complemento,
        string Bairro, string Cidade, string Uf, string? Referencia);

    internal sealed record LocalizacaoEditada(double Latitude, double Longitude);

    internal sealed record EquipamentoEditadoDto(
        Guid Id,
        TipoEquipamento Tipo,
        string Modelo,
        string? Serie,
        string? Mac,
        decimal? Valor);

    internal sealed record Pedido(
        int? NumeroOs,
        string TipoOs,
        EnderecoEditado Endereco,
        string? Cep,
        LocalizacaoEditada? Localizacao,
        string? Observacao,
        string? Plano,
        string? Protocolo,
        int? IdOrdemServicoHubsoft,
        int? IdClienteServicoHubsoft,
        DateTimeOffset? DataProgramada,
        DateOnly? DataRescisao,
        IReadOnlyList<EquipamentoEditadoDto> Equipamentos);

    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapPatch("/{id:guid}", ([AsParameters] PedidoDeComando p, Pedido corpo) =>
                ComandoDaOrdem.ExecutarAsync(p, (ordem, agora) =>
                {
                    var end = corpo.Endereco;
                    var endereco = new Endereco(
                        (end.Logradouro ?? string.Empty).Trim(),
                        string.IsNullOrWhiteSpace(end.Numero) ? "s/n" : end.Numero.Trim(),
                        string.IsNullOrWhiteSpace(end.Complemento) ? null : end.Complemento.Trim(),
                        string.IsNullOrWhiteSpace(end.Bairro) ? "(não informado)" : end.Bairro.Trim(),
                        (end.Cidade ?? string.Empty).Trim(),
                        (end.Uf ?? string.Empty).Trim().ToUpperInvariant(),
                        string.IsNullOrWhiteSpace(end.Referencia) ? null : end.Referencia.Trim());

                    var localizacao = corpo.Localizacao is { } loc
                        ? new Localizacao(loc.Latitude, loc.Longitude)
                        : (Localizacao?)null;

                    var equipamentos = (corpo.Equipamentos ?? [])
                        .Select(e => new EquipamentoEditado(
                            e.Id,
                            e.Tipo,
                            (e.Modelo ?? string.Empty).Trim(),
                            string.IsNullOrWhiteSpace(e.Serie) ? null : e.Serie.Trim(),
                            string.IsNullOrWhiteSpace(e.Mac) ? null : e.Mac.Trim(),
                            e.Valor))
                        .ToList();

                    ordem.Editar(
                        corpo.NumeroOs,
                        (corpo.TipoOs ?? string.Empty).Trim(),
                        endereco,
                        string.IsNullOrWhiteSpace(corpo.Cep) ? null : corpo.Cep.Trim(),
                        localizacao,
                        string.IsNullOrWhiteSpace(corpo.Observacao) ? null : corpo.Observacao.Trim(),
                        string.IsNullOrWhiteSpace(corpo.Plano) ? null : corpo.Plano.Trim(),
                        string.IsNullOrWhiteSpace(corpo.Protocolo) ? null : corpo.Protocolo.Trim(),
                        corpo.IdOrdemServicoHubsoft,
                        corpo.IdClienteServicoHubsoft,
                        corpo.DataProgramada,
                        corpo.DataRescisao,
                        equipamentos,
                        agora);

                    return Task.FromResult<object?>(null);
                }))
            .ComoComando(Politicas.Financeiro, "Editar todos os campos da ordem antes do encerramento (financeiro/gestor).");
}
