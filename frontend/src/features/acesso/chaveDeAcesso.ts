import { queryOptions } from '@tanstack/react-query'
import { z } from 'zod'
import { buscarJson, enviar } from '@/lib/api'
import { respostaDeLogin } from './acesso'

/**
 * Chave de acesso (passkey, WebAuthn): entra com a digital, o rosto ou o PIN do aparelho (ADR-012).
 * O servidor manda as opções no JSON padrão do WebAuthn, com os binários em base64url; aqui elas viram
 * ArrayBuffer para o navegador, e a resposta do aparelho volta para base64url.
 */

/** Celular ou computador sem suporte (navegador antigo, ou página sem HTTPS) não mostra o botão. */
export function aparelhoAceitaChave(): boolean {
  return typeof window !== 'undefined' && window.isSecureContext && 'PublicKeyCredential' in window && !!navigator.credentials
}

export function deBase64Url(texto: string): ArrayBuffer {
  const base64 = texto.replace(/-/g, '+').replace(/_/g, '/').padEnd(Math.ceil(texto.length / 4) * 4, '=')
  const binario = atob(base64)
  const bytes = new Uint8Array(binario.length)
  for (let i = 0; i < binario.length; i++) bytes[i] = binario.charCodeAt(i)
  return bytes.buffer
}

export function paraBase64Url(dados: ArrayBuffer): string {
  let binario = ''
  for (const byte of new Uint8Array(dados)) binario += String.fromCharCode(byte)
  return btoa(binario).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}

const descritor = z.object({ type: z.literal('public-key'), id: z.string(), transports: z.array(z.string()).optional() })

const opcoesDeCadastro = z.looseObject({
  challenge: z.string(),
  user: z.looseObject({ id: z.string(), name: z.string(), displayName: z.string() }),
  excludeCredentials: z.array(descritor).optional(),
})

const opcoesDeEntrada = z.looseObject({
  challenge: z.string(),
  allowCredentials: z.array(descritor).optional(),
})

const paraDescritor = (d: z.infer<typeof descritor>): PublicKeyCredentialDescriptor => ({
  type: d.type,
  id: deBase64Url(d.id),
  transports: d.transports as AuthenticatorTransport[] | undefined,
})

export function opcoesDeCriacao(json: z.infer<typeof opcoesDeCadastro>): PublicKeyCredentialCreationOptions {
  const { challenge, user, excludeCredentials, ...resto } = json
  return {
    ...(resto as unknown as Omit<PublicKeyCredentialCreationOptions, 'challenge' | 'user' | 'excludeCredentials'>),
    challenge: deBase64Url(challenge),
    user: { ...user, id: deBase64Url(user.id) },
    excludeCredentials: excludeCredentials?.map(paraDescritor),
  }
}

export function opcoesDeObtencao(json: z.infer<typeof opcoesDeEntrada>): PublicKeyCredentialRequestOptions {
  const { challenge, allowCredentials, ...resto } = json
  return {
    ...(resto as unknown as Omit<PublicKeyCredentialRequestOptions, 'challenge' | 'allowCredentials'>),
    challenge: deBase64Url(challenge),
    allowCredentials: allowCredentials?.map(paraDescritor),
  }
}

function respostaDoCadastro(credencial: PublicKeyCredential) {
  const resposta = credencial.response as AuthenticatorAttestationResponse
  return {
    id: credencial.id,
    rawId: paraBase64Url(credencial.rawId),
    type: credencial.type,
    response: {
      clientDataJSON: paraBase64Url(resposta.clientDataJSON),
      attestationObject: paraBase64Url(resposta.attestationObject),
      transports: resposta.getTransports?.() ?? [],
    },
    clientExtensionResults: credencial.getClientExtensionResults(),
  }
}

function respostaDaEntrada(credencial: PublicKeyCredential) {
  const resposta = credencial.response as AuthenticatorAssertionResponse
  return {
    id: credencial.id,
    rawId: paraBase64Url(credencial.rawId),
    type: credencial.type,
    response: {
      clientDataJSON: paraBase64Url(resposta.clientDataJSON),
      authenticatorData: paraBase64Url(resposta.authenticatorData),
      signature: paraBase64Url(resposta.signature),
      userHandle: resposta.userHandle ? paraBase64Url(resposta.userHandle) : null,
    },
    clientExtensionResults: credencial.getClientExtensionResults(),
  }
}

/** Erro do próprio aparelho, já com a mensagem para a pessoa. Cancelar não é erro: a tela só volta ao normal. */
export class ErroDaChave extends Error {
  readonly cancelado: boolean

  constructor(mensagem: string, cancelado = false) {
    super(mensagem)
    this.name = 'ErroDaChave'
    this.cancelado = cancelado
  }
}

function traduzir(erro: unknown, cadastro: boolean): never {
  if (erro instanceof DOMException) {
    if (erro.name === 'NotAllowedError' || erro.name === 'AbortError')
      throw new ErroDaChave('A chave de acesso não foi usada. Tente de novo quando quiser.', true)
    if (erro.name === 'InvalidStateError' && cadastro) throw new ErroDaChave('Este aparelho já tem uma chave de acesso sua.')
    if (erro.name === 'SecurityError') throw new ErroDaChave('A chave de acesso só funciona no endereço oficial do sistema, com HTTPS.')
    throw new ErroDaChave('Este aparelho não conseguiu usar a chave de acesso. Entre com a senha.')
  }
  throw erro
}

export const chaveDeAcesso = {
  /** Sem e-mail: o aparelho mostra as chaves que tem para este site e a pessoa escolhe. */
  entrar: async () => {
    const opcoes = await enviar('/api/acesso/chave/entrar/opcoes', undefined, opcoesDeEntrada)
    const credencial = (await navigator.credentials
      .get({ publicKey: opcoesDeObtencao(opcoes) })
      .catch((e: unknown) => traduzir(e, false))) as PublicKeyCredential | null
    if (!credencial) throw new ErroDaChave('A chave de acesso não foi usada. Tente de novo quando quiser.', true)
    return enviar('/api/acesso/chave/entrar', { credencial: respostaDaEntrada(credencial) }, respostaDeLogin)
  },

  /** No primeiro acesso de gestor e financeiro, ou em Minha conta para mais um aparelho. */
  cadastrar: async (nome?: string) => {
    const opcoes = await enviar('/api/acesso/chave/cadastro/opcoes', undefined, opcoesDeCadastro)
    const credencial = (await navigator.credentials
      .create({ publicKey: opcoesDeCriacao(opcoes) })
      .catch((e: unknown) => traduzir(e, true))) as PublicKeyCredential | null
    if (!credencial) throw new ErroDaChave('A chave de acesso não foi criada. Tente de novo quando quiser.', true)
    return enviar('/api/acesso/chave/cadastro', { credencial: respostaDoCadastro(credencial), nome: nome ?? nomeDoAparelho() }, respostaDeLogin)
  },

  remover: (id: string) => enviar(`/api/acesso/chaves/${id}/remover`),
}

export const chaveNaLista = z.object({
  id: z.uuid(),
  nome: z.string(),
  criadaEm: z.iso.datetime({ offset: true }),
  ultimoUsoEm: z.iso.datetime({ offset: true }).nullable(),
})
export type ChaveNaLista = z.infer<typeof chaveNaLista>

export const chavesQuery = queryOptions({
  queryKey: ['acesso', 'chaves'],
  queryFn: ({ signal }) => buscarJson('/api/acesso/chaves', z.array(chaveNaLista), signal),
})

/** Nome sugerido para a chave, para a pessoa reconhecer depois em Minha conta. */
export function nomeDoAparelho(agente = typeof navigator === 'undefined' ? '' : navigator.userAgent): string {
  if (/iPhone/i.test(agente)) return 'iPhone'
  if (/iPad/i.test(agente)) return 'iPad'
  if (/Android/i.test(agente)) return /Mobile/i.test(agente) ? 'Celular Android' : 'Tablet Android'
  if (/Macintosh|Mac OS X/i.test(agente)) return 'Mac'
  if (/Windows/i.test(agente)) return 'Computador Windows'
  if (/Linux/i.test(agente)) return 'Computador Linux'
  return 'Chave de acesso'
}
