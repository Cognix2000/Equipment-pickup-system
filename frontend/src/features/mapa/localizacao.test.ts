import { describe, expect, it } from 'vitest'
import { descreverSinal, deveEnviar, distanciaEmMetros } from './localizacao'

const centro = { latitude: -21.0, longitude: -38.99 }
// ~0,001° de latitude ≈ 111 m
const pertinho = { latitude: -21.0003, longitude: -38.99 }
const umaQuadra = { latitude: -21.001, longitude: -38.99 }

describe('distanciaEmMetros', () => {
  it('mede em metros', () => {
    expect(distanciaEmMetros(centro, umaQuadra)).toBeGreaterThan(105)
    expect(distanciaEmMetros(centro, umaQuadra)).toBeLessThan(117)
    expect(distanciaEmMetros(centro, centro)).toBe(0)
  })
})

describe('deveEnviar', () => {
  const ultimo = { ...centro, em: 1_000_000 }

  it('manda a primeira posição na hora', () => {
    expect(deveEnviar(null, centro, 0)).toBe(true)
  })

  it('parado, manda a cada 30 s', () => {
    expect(deveEnviar(ultimo, pertinho, ultimo.em + 20_000)).toBe(false)
    expect(deveEnviar(ultimo, pertinho, ultimo.em + 30_000)).toBe(true)
  })

  it('andando, manda antes dos 30 s, mas não mais que uma vez a cada 10 s', () => {
    expect(deveEnviar(ultimo, umaQuadra, ultimo.em + 5_000)).toBe(false)
    expect(deveEnviar(ultimo, umaQuadra, ultimo.em + 10_000)).toBe(true)
  })
})

describe('descreverSinal', () => {
  const agora = Date.parse('2026-10-31T15:00:00Z')

  it('diz há quanto tempo e se ainda vale como em campo', () => {
    expect(descreverSinal(null, agora)).toEqual({ texto: 'Ainda não abriu o app', recente: false })
    expect(descreverSinal('2026-10-31T14:59:30Z', agora)).toEqual({ texto: 'Agora', recente: true })
    expect(descreverSinal('2026-10-31T14:50:00Z', agora)).toEqual({ texto: 'Há 10 min', recente: true })
    expect(descreverSinal('2026-10-31T14:30:00Z', agora)).toEqual({ texto: 'Há 30 min', recente: false })
    expect(descreverSinal('2026-10-31T12:00:00Z', agora)).toEqual({ texto: 'Há 3 h', recente: false })
    expect(descreverSinal('2026-10-29T12:00:00Z', agora)).toEqual({ texto: 'Em 29/10', recente: false })
  })
})
