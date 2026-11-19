import { describe, expect, it } from 'vitest'
import { distanciaCurta, distanciaKm, ordenarOrdens, pontuacaoDeUrgencia } from './ordenacao'
import type { Ordem } from './tipos'

// Ordem mínima para teste; os campos não usados pela ordenação ficam fixos.
function ordem(sobrepor: Partial<Ordem>): Ordem {
  return {
    id: crypto.randomUUID(),
    numeroOs: null,
    tipoOs: 'RETIRADA',
    status: 'atribuida',
    cliente: 'ASSINANTE SIMULADO',
    endereco: { logradouro: 'RUA A', numero: '1', complemento: null, bairro: 'CENTRO', cidade: 'CIDADE SIMULADA', uf: 'MG', referencia: null },
    localizacao: null,
    observacao: null,
    plano: null,
    carenciaAte: '2026-11-13T10:00:00-03:00',
    aguardaBaixaNoHubsoft: false,
    equipamentos: [],
    telefones: null,
    historico: null,
    fotos: null,
    semContatoNoPrazo: false,
    visitasSemSucesso: 0,
    ...sobrepor,
  }
}

describe('distanciaKm', () => {
  it('retorna 0 para pontos iguais', () => {
    expect(distanciaKm({ latitude: -20.1, longitude: -45.5 }, { latitude: -20.1, longitude: -45.5 })).toBeCloseTo(0, 5)
  })

  it('aproxima bem para distâncias curtas (dentro da mesma cidade)', () => {
    // ~1,11 km entre dois pontos 0,01° na latitude (medida padrão do Haversine).
    const d = distanciaKm({ latitude: -20.0, longitude: -45.0 }, { latitude: -20.01, longitude: -45.0 })
    expect(d).toBeGreaterThan(1.0)
    expect(d).toBeLessThan(1.2)
  })
})

describe('pontuacaoDeUrgencia', () => {
  it('ordem sem contato no prazo vale mais que a sem nada', () => {
    expect(pontuacaoDeUrgencia(ordem({ semContatoNoPrazo: true }))).toBeGreaterThan(pontuacaoDeUrgencia(ordem({})))
  })
  it('ordem com 2 visitas sem sucesso ganha peso', () => {
    expect(pontuacaoDeUrgencia(ordem({ visitasSemSucesso: 2 }))).toBeGreaterThan(pontuacaoDeUrgencia(ordem({ visitasSemSucesso: 0 })))
  })
})

describe('ordenarOrdens', () => {
  const longe = ordem({ localizacao: { latitude: -21.0, longitude: -45.0 } })
  const perto = ordem({ localizacao: { latitude: -20.001, longitude: -45.001 } })
  const semGps = ordem({ localizacao: null })
  const meu = { latitude: -20.0, longitude: -45.0, precisaoM: null }

  it('proximidade coloca a mais próxima primeiro e a sem GPS no fim', () => {
    const resultado = ordenarOrdens([longe, perto, semGps], 'proximidade', meu)
    expect(resultado.map((o) => o.id)).toEqual([perto.id, longe.id, semGps.id])
  })

  it('sem GPS do técnico, proximidade cai para urgência', () => {
    const urgente = ordem({ semContatoNoPrazo: true })
    const comum = ordem({})
    const resultado = ordenarOrdens([comum, urgente], 'proximidade', null)
    expect(resultado[0]).toBe(urgente)
  })

  it('urgência coloca a com mais pontos primeiro', () => {
    const urgente = ordem({ semContatoNoPrazo: true, visitasSemSucesso: 2 })
    const normal = ordem({})
    const resultado = ordenarOrdens([normal, urgente], 'urgencia', null)
    expect(resultado[0]).toBe(urgente)
  })

  it('bairro ordena alfabeticamente', () => {
    const b = ordem({ endereco: { ...ordem({}).endereco, bairro: 'ZONA SUL' } })
    const a = ordem({ endereco: { ...ordem({}).endereco, bairro: 'ALFA' } })
    const resultado = ordenarOrdens([b, a], 'bairro', null)
    expect(resultado[0]).toBe(a)
  })
})

describe('distanciaCurta', () => {
  it('retorna null sem GPS ou sem localização da ordem', () => {
    expect(distanciaCurta(ordem({ localizacao: null }), { latitude: 0, longitude: 0, precisaoM: null })).toBeNull()
    expect(distanciaCurta(ordem({ localizacao: { latitude: 1, longitude: 1 } }), null)).toBeNull()
  })
  it('usa metros para menos de 1 km', () => {
    // ~111 m entre dois pontos 0,001° na latitude.
    const o = ordem({ localizacao: { latitude: -20.001, longitude: -45.0 } })
    const texto = distanciaCurta(o, { latitude: -20.0, longitude: -45.0, precisaoM: null })!
    expect(texto).toMatch(/\d{2,3} m$/)
  })
  it('usa quilômetros para 1 km ou mais, com vírgula', () => {
    const o = ordem({ localizacao: { latitude: -20.05, longitude: -45.0 } })
    const texto = distanciaCurta(o, { latitude: -20.0, longitude: -45.0, precisaoM: null })!
    expect(texto).toMatch(/\d+,\d km$/)
  })
})
