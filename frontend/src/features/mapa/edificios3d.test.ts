import { describe, expect, it, vi } from 'vitest'
import { aplicarEstiloImersivo, desabilitarEdificios3D, habilitarEdificios3D, reabilitarEdificios3D } from './baseDoMapa'

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

describe('aplicarEstiloImersivo', () => {
  function mapaComEstilo(layers: { id: string; type: string }[]) {
    const paint: Record<string, Record<string, string>> = {}
    return {
      isStyleLoaded: () => true,
      once: (_e: string, cb: () => void) => cb(),
      getStyle: () => ({ layers }),
      setPaintProperty: (id: string, prop: string, valor: string) => {
        paint[id] ??= {}
        paint[id]![prop] = valor
      },
      setSky: vi.fn(),
      _paint: paint,
    }
  }

  it('recolore background, água, parque, bairro e rua para a paleta Imersivo (Opção 2)', () => {
    const mapa = mapaComEstilo([
      { id: 'background', type: 'background' },
      { id: 'water', type: 'fill' },
      { id: 'landuse_park', type: 'fill' },
      { id: 'landuse_residential', type: 'fill' },
      { id: 'road_motorway', type: 'line' },
      { id: 'road_minor', type: 'line' },
    ])
    aplicarEstiloImersivo(mapa as unknown as Parameters<typeof aplicarEstiloImersivo>[0])
    // Bege claro no fundo.
    expect(mapa._paint.background!['background-color']).toBe('#f1e6d2')
    // Azul suave na água, quente nos usos de solo.
    expect(mapa._paint.water!['fill-color']).toBe('#9cc6d9')
    expect(mapa._paint.landuse_park!['fill-color']).toBe('#d9d5a8')
    expect(mapa._paint.landuse_residential!['fill-color']).toBe('#e8dcc0')
    // Avenida amarelada, rua secundária creme.
    expect(mapa._paint.road_motorway!['line-color']).toBe('#ffe8a8')
    expect(mapa._paint.road_minor!['line-color']).toBe('#f8ead0')
    // Céu azulado quando o mapa inclinar.
    expect(mapa.setSky).toHaveBeenCalled()
  })

  it('não quebra se setSky não existir na versão do MapLibre (tipos antigos)', () => {
    const mapa = {
      isStyleLoaded: () => true,
      once: (_e: string, cb: () => void) => cb(),
      getStyle: () => ({ layers: [{ id: 'background', type: 'background' }] }),
      setPaintProperty: () => {},
      setSky: () => {
        throw new Error('sem setSky nessa versão')
      },
    }
    expect(() => aplicarEstiloImersivo(mapa as unknown as Parameters<typeof aplicarEstiloImersivo>[0])).not.toThrow()
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
