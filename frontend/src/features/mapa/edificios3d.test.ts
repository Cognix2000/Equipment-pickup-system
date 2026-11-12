import { describe, expect, it, vi } from 'vitest'
import { desabilitarEdificios3D, habilitarEdificios3D, reabilitarEdificios3D } from './baseDoMapa'

/**
 * Um fake minimalista do MapLibre Map. Guarda o que foi adicionado e permite inspecionar as chamadas.
 * Não renderiza nada: só valida a sequência de operações sobre os layers.
 */
function mapaMock(options: { comSourceOpenMapTiles?: boolean; comLayerPlano?: boolean; carregado?: boolean } = {}) {
  const { comSourceOpenMapTiles = true, comLayerPlano = true, carregado = true } = options
  const layers: Record<string, { visibility: string }> = {}
  if (comLayerPlano) layers.building = { visibility: 'visible' }
  const operacoes: { tipo: string; dados?: unknown }[] = []
  return {
    getLayer: (id: string) => (id in layers ? { id } : undefined),
    addLayer: (spec: { id: string }) => {
      layers[spec.id] = { visibility: 'visible' }
      operacoes.push({ tipo: 'addLayer', dados: spec })
    },
    getSource: (id: string) => (id === 'openmaptiles' && comSourceOpenMapTiles ? {} : undefined),
    setLayoutProperty: (id: string, _prop: string, valor: string) => {
      if (layers[id]) layers[id].visibility = valor
      operacoes.push({ tipo: 'setLayoutProperty', dados: { id, valor } })
    },
    setLight: vi.fn(() => operacoes.push({ tipo: 'setLight' })),
    isStyleLoaded: () => carregado,
    once: (_evento: string, cb: () => void) => cb(),
    _layers: layers,
    _operacoes: operacoes,
  }
}

describe('habilitarEdificios3D', () => {
  it('no estilo simulado não faz nada (não há prédio no fundo)', () => {
    const mapa = mapaMock()
    // O cast evita a brincadeira de completar toda a interface do MapLibre; o fake atende o que a função chama.
    habilitarEdificios3D(mapa as unknown as Parameters<typeof habilitarEdificios3D>[0], 'simulado')
    expect(mapa._operacoes).toHaveLength(0)
  })

  it('no estilo real adiciona a extrusão, esconde o prédio plano e configura a luz', () => {
    const mapa = mapaMock()
    habilitarEdificios3D(mapa as unknown as Parameters<typeof habilitarEdificios3D>[0], 'real')
    expect(mapa._layers.building?.visibility).toBe('none')
    expect(mapa._layers['edificios-3d']).toBeDefined()
    expect(mapa.setLight).toHaveBeenCalled()
  })

  it('duas chamadas não duplicam o layer', () => {
    const mapa = mapaMock()
    habilitarEdificios3D(mapa as unknown as Parameters<typeof habilitarEdificios3D>[0], 'real')
    const adicoes = mapa._operacoes.filter((o) => o.tipo === 'addLayer').length
    habilitarEdificios3D(mapa as unknown as Parameters<typeof habilitarEdificios3D>[0], 'real')
    expect(mapa._operacoes.filter((o) => o.tipo === 'addLayer').length).toBe(adicoes)
  })

  it('não quebra se o estilo real não tiver a source openmaptiles (estilo customizado)', () => {
    const mapa = mapaMock({ comSourceOpenMapTiles: false, comLayerPlano: false })
    expect(() => habilitarEdificios3D(mapa as unknown as Parameters<typeof habilitarEdificios3D>[0], 'real')).not.toThrow()
    expect(mapa._layers['edificios-3d']).toBeUndefined()
  })
})

describe('desabilitarEdificios3D / reabilitarEdificios3D', () => {
  it('desligar esconde a extrusão e mostra o 2D; religar inverte', () => {
    const mapa = mapaMock()
    habilitarEdificios3D(mapa as unknown as Parameters<typeof habilitarEdificios3D>[0], 'real')
    desabilitarEdificios3D(mapa as unknown as Parameters<typeof desabilitarEdificios3D>[0])
    expect(mapa._layers['edificios-3d']?.visibility).toBe('none')
    expect(mapa._layers.building?.visibility).toBe('visible')
    reabilitarEdificios3D(mapa as unknown as Parameters<typeof reabilitarEdificios3D>[0])
    expect(mapa._layers['edificios-3d']?.visibility).toBe('visible')
    expect(mapa._layers.building?.visibility).toBe('none')
  })
})
