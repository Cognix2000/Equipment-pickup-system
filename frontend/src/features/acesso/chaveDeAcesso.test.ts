import { describe, expect, it } from 'vitest'
import { deBase64Url, nomeDoAparelho, opcoesDeCriacao, opcoesDeObtencao, paraBase64Url } from './chaveDeAcesso'

const bytes = (b: ArrayBuffer | BufferSource) => Array.from(new Uint8Array(b as ArrayBuffer))

describe('base64url do WebAuthn', () => {
  it('ida e volta sem perder byte, inclusive os que viram - e _', () => {
    const original = new Uint8Array([0, 1, 250, 251, 252, 253, 254, 255, 62, 63]).buffer
    const texto = paraBase64Url(original)
    expect(texto).not.toMatch(/[+/=]/)
    expect(bytes(deBase64Url(texto))).toEqual(bytes(original))
  })

  it('aceita texto sem o preenchimento =', () => {
    expect(bytes(deBase64Url('AQI'))).toEqual([1, 2])
  })
})

describe('opções do servidor para o navegador', () => {
  it('cadastro: desafio, usuário e chaves já cadastradas viram binário; o resto passa igual', () => {
    const opcoes = opcoesDeCriacao({
      challenge: 'AQID',
      user: { id: 'BAU', name: 'financeiro.demo@exemplo.invalid', displayName: 'Financeiro' },
      excludeCredentials: [{ type: 'public-key', id: 'Bgc' }],
      rp: { id: 'localhost', name: 'Retirada Netwise' },
      attestation: 'none',
    })
    expect(bytes(opcoes.challenge)).toEqual([1, 2, 3])
    expect(bytes(opcoes.user.id)).toEqual([4, 5])
    expect(bytes(opcoes.excludeCredentials![0]!.id)).toEqual([6, 7])
    expect(opcoes.rp).toEqual({ id: 'localhost', name: 'Retirada Netwise' })
    expect(opcoes.attestation).toBe('none')
  })

  it('entrada: sem lista de chaves, o aparelho oferece as que tem', () => {
    const opcoes = opcoesDeObtencao({ challenge: 'AQID', allowCredentials: [], userVerification: 'required' })
    expect(bytes(opcoes.challenge)).toEqual([1, 2, 3])
    expect(opcoes.allowCredentials).toEqual([])
    expect(opcoes.userVerification).toBe('required')
  })
})

describe('nome sugerido para a chave', () => {
  it.each([
    ['Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X)', 'iPhone'],
    ['Mozilla/5.0 (Linux; Android 15; Pixel 9) Mobile Safari/537.36', 'Celular Android'],
    ['Mozilla/5.0 (Linux; Android 15; Tab S9) Safari/537.36', 'Tablet Android'],
    ['Mozilla/5.0 (Windows NT 10.0; Win64; x64)', 'Computador Windows'],
    ['Mozilla/5.0 (Macintosh; Intel Mac OS X 15_0)', 'Mac'],
    ['', 'Chave de acesso'],
  ])('%s → %s', (agente, nome) => {
    expect(nomeDoAparelho(agente)).toBe(nome)
  })
})
