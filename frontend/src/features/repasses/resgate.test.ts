import { describe, expect, it } from 'vitest'
import { mesDe } from './resgate'

describe('mesDe', () => {
  it('usa o mês de Brasília', () => {
    expect(mesDe(new Date('2026-11-10T12:00:00Z'))).toBe('2026-11')
    // 01/12 à 01h UTC ainda é 30/11 em Brasília
    expect(mesDe(new Date('2026-12-01T01:00:00Z'))).toBe('2026-11')
  })
})
