using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Retirada.Comum;
using Retirada.Ordens.Dominio;
using Retirada.Ordens.Infraestrutura;
using Retirada.Ordens.Infraestrutura.Fotos;
using Retirada.Ordens.Portas;

namespace Retirada.Ordens.Casos;

/// <summary>
/// RF12, RN14 e UC04: o técnico registra a série de cada item, o GPS e as fotos que já enviou.
/// Série diferente da cadastrada não impede: fica como divergência para o estoque, com a foto da etiqueta.
/// Ordem cancelada bloqueia (RN07).
/// </summary>
internal static class RegistrarRetirada
{
    /// <param name="Id">O id que o envio da foto devolveu.</param>
    /// <param name="CapturadaEm">Hora em que a foto foi tirada, pelo relógio do aparelho.</param>
    internal sealed record FotoDaRetiradaDto(Guid Id, TipoDeFoto Tipo, Guid? EquipamentoId, DateTimeOffset CapturadaEm);

    internal sealed record PedidoDeRetirada(IReadOnlyList<ItemLido> Itens, Gps Gps, IReadOnlyList<FotoDaRetiradaDto>? Fotos);

    private sealed record FotoConferida(FotoDaRetiradaDto Foto, IReadOnlyDictionary<string, string> Metadados);

    public static void Mapear(IEndpointRouteBuilder grupo) =>
        grupo.MapPost("/{id:guid}/retirada", ([AsParameters] PedidoDeComando p, PedidoDeRetirada corpo, IArmazenamentoDeFotos armazenamento) =>
            {
                var fotos = corpo.Fotos ?? [];
                var conferidas = new List<FotoConferida>();
                return ComandoDaOrdem.ExecutarAsync(p,
                    async (ordem, agora) =>
                    {
                        var series = ComandoDaOrdem.SeriesLidas(ordem, corpo.Itens, "RF12");
                        if (fotos.Select(f => f.Id).Distinct().Count() != fotos.Count)
                            throw new RegraDeNegocioException("RN14", "A mesma foto foi enviada duas vezes.");
                        // A foto tem de ter sido enviada por este técnico, para esta ordem.
                        foreach (var foto in fotos)
                        {
                            var metadados = await armazenamento.MetadadosAsync(Chaves.Pendente(ordem.Id, foto.Id), p.Ct);
                            if (metadados is null || metadados.GetValueOrDefault(MetadadosDaFoto.Autor) != p.Usuario.Id().ToString())
                                throw new RegraDeNegocioException("RN14", "Uma das fotos não chegou ao servidor. Tire e envie de novo.");
                            conferidas.Add(new FotoConferida(foto, metadados));
                        }

                        ordem.RegistrarRetirada(series, corpo.Gps.Localizacao(), agora,
                            [.. fotos.Select(f => new FotoDaRetirada(f.Tipo, f.EquipamentoId))]);
                        return new { series = ComandoDaOrdem.ParaRegistro(series), fotos = fotos.Count };
                    },
                    corpo.Gps,
                    gravarJunto: (ordem, _) => GravarFotosAsync(p, armazenamento, ordem, conferidas, corpo.Gps),
                    depoisDeGravar: async () =>
                    {
                        foreach (var c in conferidas)
                            await armazenamento.ApagarAsync(Chaves.Pendente(p.Id, c.Foto.Id), CancellationToken.None);
                    });
            })
            .ComoComando(Politicas.Campo, "Registrar a retirada, com as fotos já enviadas (RF12, RN14)");

    /// <summary>
    /// Copia cada foto para o lugar definitivo e grava a evidência ligada ao evento da retirada (só acréscimo, RN12).
    /// Se a transação falhar depois da cópia, sobra um arquivo sem evidência: não aparece em lugar nenhum,
    /// e a foto pendente continua lá para a nova tentativa.
    /// </summary>
    private static async Task GravarFotosAsync(
        PedidoDeComando p, IArmazenamentoDeFotos armazenamento, Ordem ordem, List<FotoConferida> fotos, Gps gps)
    {
        if (fotos.Count == 0) return;
        var eventoId = await p.Db.Database
            .SqlQuery<long>($"SELECT id AS \"Value\" FROM retirada.evento_ordem WHERE chave_idempotencia = {p.ChaveDeIdempotencia!.Value}")
            .SingleAsync(p.Ct);
        var autor = p.Usuario.Id();

        foreach (var (foto, m) in fotos)
        {
            var chave = Chaves.DaOrdem(ordem.Id, foto.Id);
            await armazenamento.CopiarAsync(Chaves.Pendente(ordem.Id, foto.Id), chave, p.Ct);
            var tipo = OrdensDbContext.ParaSnake(foto.Tipo.ToString());
            await p.Db.Database.ExecuteSqlAsync($"""
                INSERT INTO retirada.evidencia
                    (id, ordem_id, evento_id, equipamento_id, tipo, arquivo_chave, sha256, bytes, mime, largura, altura,
                     capturada_em, local, precisao_m, autor_id)
                VALUES ({foto.Id}, {ordem.Id}, {eventoId}, {foto.EquipamentoId}, {tipo}, {chave}, {m[MetadadosDaFoto.Sha256]},
                        {int.Parse(m[MetadadosDaFoto.Bytes], CultureInfo.InvariantCulture)}, 'image/jpeg',
                        {short.Parse(m[MetadadosDaFoto.Largura], CultureInfo.InvariantCulture)}, {short.Parse(m[MetadadosDaFoto.Altura], CultureInfo.InvariantCulture)},
                        {foto.CapturadaEm.ToUniversalTime()},
                        ST_SetSRID(ST_MakePoint({gps.Longitude}, {gps.Latitude}), 4326)::geography,
                        {(float?)gps.PrecisaoM}, {autor})
                """, p.Ct);
        }
    }
}
