using Microsoft.EntityFrameworkCore;
using Retirada.Ordens.Dominio;
using Retirada.Ordens.Infraestrutura;

namespace Retirada.Ordens.Casos;

/// <summary>O que as consultas devolvem para as telas. Compartilhado pelos casos de leitura.</summary>
internal static class Projecoes
{
    /// <summary>Uma ordem pronta para a tela, ou nulo se não existe.</summary>
    public static async Task<OrdemDto?> CarregarOrdemAsync(OrdensDbContext db, Guid id, CancellationToken ct)
    {
        var linha = await db.Ordens.AsNoTracking()
            .Where(o => o.Id == id)
            .Join(db.Clientes, o => o.ClienteId, c => c.Id, (o, c) => new { Ordem = o, Cliente = c.Nome })
            .SingleOrDefaultAsync(ct);
        if (linha is null) return null;

        var equipamentos = await CarregarEquipamentosAsync(db, [id], ct);
        // RF07: o detalhe traz os telefones do cliente e o histórico; a lista não, para ficar leve.
        var telefones = await db.Database.SqlQuery<TelefoneDto>($"""
            SELECT id AS "Id", numero_e164 AS "Numero", tipo AS "Tipo", formato_valido AS "FormatoValido",
                   linha AS "Linha", whatsapp AS "Whatsapp"
            FROM retirada.telefone_cliente WHERE cliente_id = {linha.Ordem.ClienteId}
            ORDER BY formato_valido DESC, tipo = 'celular' DESC, numero_e164
            """).ToListAsync(ct);
        var historico = await db.Database.SqlQuery<EventoDto>($"""
            SELECT tipo AS "Tipo", ocorrido_em AS "OcorridoEm", motivo AS "Motivo", status_para AS "StatusPara", ator_papel AS "AtorPapel"
            FROM retirada.evento_ordem WHERE ordem_id = {id} ORDER BY seq DESC
            """).ToListAsync(ct);
        var fotos = await db.Database.SqlQuery<FotoBruta>($"""
            SELECT id AS "Id", tipo AS "Tipo", equipamento_id AS "EquipamentoId", capturada_em AS "CapturadaEm"
            FROM retirada.evidencia WHERE ordem_id = {id} ORDER BY capturada_em, id
            """).ToListAsync(ct);
        return OrdemDto.De(linha.Ordem, linha.Cliente, equipamentos[id]) with
        {
            Telefones = telefones,
            Historico = historico,
            Fotos = [.. fotos.Select(f => new FotoDto(f.Id, Enum.Parse<TipoDeFoto>(f.Tipo.Replace("_", string.Empty), true), f.EquipamentoId, f.CapturadaEm))],
        };
    }

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

/// <summary>
/// RN05: telefone já em E.164. O fora do padrão vem marcado, para a tela não oferecer a ligação.
/// Linha e WhatsApp vêm do resultado dos contatos registrados (RN10), nunca de consulta escondida.
/// </summary>
internal sealed record TelefoneDto(Guid Id, string Numero, string Tipo, bool FormatoValido, string Linha, string Whatsapp);

internal sealed record FotoBruta(Guid Id, string Tipo, Guid? EquipamentoId, DateTimeOffset CapturadaEm);

/// <summary>RN14: uma foto da ordem. O arquivo sai em GET /api/ordens/{id}/fotos/{fotoId}.</summary>
internal sealed record FotoDto(Guid Id, TipoDeFoto Tipo, Guid? EquipamentoId, DateTimeOffset CapturadaEm);

/// <summary>RF07 e RF16: uma linha do histórico. Quem fez aparece só pelo papel.</summary>
internal sealed record EventoDto(string Tipo, DateTimeOffset OcorridoEm, string? Motivo, string? StatusPara, string? AtorPapel);

internal sealed record EquipamentoDto(Guid Id, TipoEquipamento Tipo, string Modelo, string? NumeroSerie, SituacaoEquipamento Situacao);

/// <param name="SemContatoNoPrazo">Só na lista (RN09): passou do prazo de resposta sem nenhuma tentativa de
/// contato, então não encerra sozinha e o financeiro precisa agir.</param>
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
    Guid? ResponsavelId,
    short VisitasSemSucesso,
    bool AguardaBaixaNoHubsoft,
    IReadOnlyList<EquipamentoDto> Equipamentos,
    IReadOnlyList<TelefoneDto>? Telefones = null,
    IReadOnlyList<EventoDto>? Historico = null,
    IReadOnlyList<FotoDto>? Fotos = null,
    bool? SemContatoNoPrazo = null)
{
    public static OrdemDto De(Ordem o, string cliente, IEnumerable<Equipamento> equipamentos) => new(
        o.Id, o.NumeroOs, o.TipoOs, o.Status, cliente, o.Endereco, o.Localizacao, o.Observacao,
        o.CarenciaAte, o.ResponsavelId, o.VisitasSemSucesso, o.AguardaBaixaNoHubsoft,
        [.. equipamentos.Select(e => new EquipamentoDto(e.Id, e.Tipo, e.Modelo, e.NumeroSerie?.Valor, e.Situacao))]);
}
