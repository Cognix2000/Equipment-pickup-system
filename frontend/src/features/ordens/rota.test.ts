import { describe, expect, it } from 'vitest'
import { ordemMaria } from './exemplos'
import { linksDeRota } from './rota'

describe('linksDeRota', () => {
  it('com coordenada, Google Maps e Waze vão ao ponto exato', () => {
    const rota = linksDeRota({ ...ordemMaria, localizacao: { latitude: -21.001, longitude: -38.99 } })
    expect(rota.googleMaps).toBe('https://www.google.com/maps/dir/?api=1&destination=-21.001%2C-38.99')
    expect(rota.waze).toBe('https://waze.com/ul?ll=-21.001%2C-38.99&navigate=yes')
  })

  it('sem coordenada, vão ao endereço escrito, com o bairro', () => {
    const rota = linksDeRota({ ...ordemMaria, localizacao: null })
    const { logradouro, bairro } = ordemMaria.endereco
    const waze = decodeURIComponent(rota.waze)
    expect(waze).toContain(logradouro)
    expect(waze).toContain(bairro)
    expect(waze).toContain('&navigate=yes')
    expect(decodeURIComponent(rota.googleMaps)).toContain(bairro)
  })
})
