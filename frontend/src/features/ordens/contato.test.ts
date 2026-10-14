import { describe, expect, it } from 'vitest'
import { formatarTelefone, janelaDeContato } from './contato'

// Datas em UTC: Brasília é UTC-3.
describe('janela de contato (RN10)', () => {
  it.each([
    ['segunda 8h', '2026-10-12T11:00:00Z', true],
    ['segunda 7h59', '2026-10-12T10:59:00Z', false],
    ['sábado 17h59', '2026-10-17T20:59:00Z', true],
    ['sábado 18h', '2026-10-17T21:00:00Z', false],
    ['domingo 10h', '2026-10-18T13:00:00Z', false],
  ])('%s', (_, iso, permitido) => {
    const janela = janelaDeContato(new Date(iso))
    expect(janela.permitido).toBe(permitido)
    expect(janela.aviso === null).toBe(permitido)
  })
})

describe('telefone para a tela', () => {
  it.each([
    ['+5537991234567', '(37) 99123-4567'],
    ['+553732214567', '(37) 3221-4567'],
    ['+55009123', '+55009123'],
  ])('%s', (e164, esperado) => expect(formatarTelefone(e164)).toBe(esperado))
})
