import type { Gps } from './comandos'
import type { Ordem } from './tipos'

/** Critérios de ordenação da lista do técnico. */
export type CriterioDeOrdenacao = 'proximidade' | 'urgencia' | 'bairro'

/**
 * Distância em linha reta (fórmula de Haversine) entre dois pontos em quilômetros. Suficiente para a tela do
 * técnico: a lista precisa de ordem relativa, não de quilometragem de rota (quem traça rota é o Waze/Maps).
 */
export function distanciaKm(a: { latitude: number; longitude: number }, b: { latitude: number; longitude: number }): number {
  const R = 6371
  const toRad = (x: number) => (x * Math.PI) / 180
  const dLat = toRad(b.latitude - a.latitude)
  const dLon = toRad(b.longitude - a.longitude)
  const senoLat = Math.sin(dLat / 2)
  const senoLon = Math.sin(dLon / 2)
  const h = senoLat * senoLat + Math.cos(toRad(a.latitude)) * Math.cos(toRad(b.latitude)) * senoLon * senoLon
  return 2 * R * Math.asin(Math.min(1, Math.sqrt(h)))
}

/**
 * Nota de urgência da ordem com o que a API já devolve. Quanto maior, mais urgente — para o técnico decidir
 * qual pegar primeiro. A fórmula (pesos) é fácil de ajustar: os dados vêm do próprio DTO da listagem.
 *
 * - `semContatoNoPrazo` (RN09): a ordem está a um passo de encerrar sem retirada → peso 100.
 * - `visitasSemSucesso`: 3 visitas encerram (RN09), então cada visita acumulada vale 30.
 *
 * Se depois quisermos trazer tempo desde a rescisão ou valor dos equipamentos, incluímos aqui e expomos na API.
 */
export function pontuacaoDeUrgencia(o: Ordem): number {
  return (o.semContatoNoPrazo ? 100 : 0) + (o.visitasSemSucesso ?? 0) * 30
}

/**
 * Ordena as ordens pela regra do critério. Sem GPS do técnico, "proximidade" cai para "urgência" (não faz
 * sentido fingir distância). Sem `localizacao` na ordem, ela vai para o fim do grupo, mantendo a ordem interna.
 */
export function ordenarOrdens(ordens: Ordem[], criterio: CriterioDeOrdenacao, meuGps: Gps | null): Ordem[] {
  if (criterio === 'urgencia') {
    return [...ordens].sort((a, b) => pontuacaoDeUrgencia(b) - pontuacaoDeUrgencia(a))
  }
  if (criterio === 'proximidade') {
    if (!meuGps) return ordenarOrdens(ordens, 'urgencia', null)
    const comDistancia = ordens.map((o) => ({
      ordem: o,
      dist: o.localizacao ? distanciaKm(meuGps, o.localizacao) : Number.POSITIVE_INFINITY,
    }))
    comDistancia.sort((a, b) => a.dist - b.dist)
    return comDistancia.map((c) => c.ordem)
  }
  // "bairro": agrupa pelo bairro (ordem alfabética) e, dentro dele, mantém a ordem recebida.
  return [...ordens].sort((a, b) => a.endereco.bairro.localeCompare(b.endereco.bairro, 'pt-BR'))
}

/** Distância curtinha para mostrar no card: "1,2 km" / "750 m" / "—" sem GPS. */
export function distanciaCurta(ordem: Ordem, meuGps: Gps | null): string | null {
  if (!meuGps || !ordem.localizacao) return null
  const km = distanciaKm(meuGps, ordem.localizacao)
  return km < 1 ? `${Math.round(km * 1000)} m` : `${km.toFixed(1).replace('.', ',')} km`
}
