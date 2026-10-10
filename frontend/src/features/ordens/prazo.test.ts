import { describe, expect, it } from 'vitest'
import { descreverPrazo } from './prazo'

// 08/10/2026 às 22h em Brasília = 09/10 01h UTC: o dia que vale é o de Brasília (RNF09).
const agora = new Date('2026-10-09T01:00:00Z')

describe('descreverPrazo', () => {
  it('diz há quantos dias venceu', () => {
    expect(descreverPrazo('2026-10-07T12:00:00Z', agora)).toEqual({ texto: 'Prazo venceu há 1 dia', vencido: true })
    expect(descreverPrazo('2026-10-05T12:00:00Z', agora)).toEqual({ texto: 'Prazo venceu há 3 dias', vencido: true })
  })

  it('usa hoje e amanhã pelo calendário de Brasília', () => {
    expect(descreverPrazo('2026-10-08T23:00:00-03:00', agora).texto).toBe('Prazo hoje')
    expect(descreverPrazo('2026-10-09T10:00:00-03:00', agora).texto).toBe('Prazo amanhã')
  })
})
