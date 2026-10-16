import { describe, expect, it } from 'vitest'
import { normalizarSerie } from './comandos'

// Os mesmos casos de NumeroSerie no backend (OrdemTestes): a tela e o servidor precisam concordar.
describe('normalizarSerie', () => {
  it('ignora espaço e caixa', () => {
    expect(normalizarSerie(' hwtc 1a2b3c4d ')).toBe('HWTC1A2B3C4D')
  })

  it('lê o SN GPON em hexadecimal como o SN da etiqueta', () => {
    expect(normalizarSerie('485754437C8D9E0F')).toBe('HWTC7C8D9E0F')
    expect(normalizarSerie(' 48575443 7c8d9e0f ')).toBe('HWTC7C8D9E0F')
    expect(normalizarSerie('5A544547A1B2C3D4')).toBe('ZTEGA1B2C3D4')
  })

  it('deixa as outras séries como estão', () => {
    expect(normalizarSerie('0123456789ABCDEF')).toBe('0123456789ABCDEF')
    expect(normalizarSerie('485754437C8D9E0')).toBe('485754437C8D9E0')
    expect(normalizarSerie('485754437C8D9E0G')).toBe('485754437C8D9E0G')
    expect(normalizarSerie('22361A2B3C4D5')).toBe('22361A2B3C4D5')
  })
})
