using Microsoft.EntityFrameworkCore;
using Retirada.Ordens.Dominio;
using Retirada.Ordens.Infraestrutura;

namespace Retirada.Ordens.Casos;

/// <summary>O que as consultas devolvem para as telas. Compartilhado pelos casos de leitura.</summary>
internal static class Projecoes
{
    public static async Task<ILookup<Guid, Equipamento>> CarregarEquipamentosAsync(
        OrdensDbContext db, List<Guid> ids, CancellationToken ct)
    {
        var itens = await db.Set<Equipamento>().AsNoTracking()
            .Where(e => ids.Contains(EF.Property<Guid>(e, "OrdemId")))
            .Select(e => new { OrdemId = EF.Property<Guid>(e, "OrdemId"), Item = e })
            .ToListAsync(ct);
        return itens.ToLookup(x => x.OrdemId, x => x.Item);
    }
}

internal sealed record EquipamentoDto(Guid Id, TipoEquipamento Tipo, string Modelo, string? NumeroSerie, SituacaoEquipamento Situacao);

internal sealed record OrdemDto(
    Guid Id,
    int? NumeroOs,
    string TipoOs,
    StatusOrdem Status,
    string Cliente,
    Endereco Endereco,
    Localizacao? Localizacao,
    string? Observacao,
    DateTimeOffset CarenciaAte,
    bool AguardaBaixaNoHubsoft,
    IReadOnlyList<EquipamentoDto> Equipamentos)
{
    public static OrdemDto De(Ordem o, string cliente, IEnumerable<Equipamento> equipamentos) => new(
        o.Id, o.NumeroOs, o.TipoOs, o.Status, cliente, o.Endereco, o.Localizacao, o.Observacao,
        o.CarenciaAte, o.AguardaBaixaNoHubsoft,
        [.. equipamentos.Select(e => new EquipamentoDto(e.Id, e.Tipo, e.Modelo, e.NumeroSerie?.Valor, e.Situacao))]);
}
