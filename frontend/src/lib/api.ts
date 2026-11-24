import type { z } from 'zod'

/** Erro da API no formato ProblemDetails; `codigo` aponta a regra de negócio (ex.: RN07). */
export class ErroDaApi extends Error {
  readonly status: number
  readonly codigo?: string

  constructor(status: number, mensagem: string, codigo?: string) {
    super(mensagem)
    this.name = 'ErroDaApi'
    this.status = status
    this.codigo = codigo
  }
}

/** ADR-010: todo comando leva este cabeçalho; outro site não consegue mandá-lo (proteção contra CSRF). */
const cabecalhoAntiCsrf = { 'X-Retirada': '1' }

async function lerErro(resposta: Response): Promise<never> {
  const problema = (await resposta.json().catch(() => null)) as { title?: string; codigo?: string; errors?: Record<string, string[]> } | null
  const detalhe = problema?.errors ? Object.values(problema.errors).flat()[0] : undefined
  const padrao = resposta.status === 429 ? 'Muitas tentativas. Espere um minuto e tente de novo.' : 'Não foi possível concluir. Tente de novo.'
  throw new ErroDaApi(resposta.status, detalhe ?? problema?.title ?? padrao, problema?.codigo)
}

/**
 * Busca JSON e valida com o schema: se a API mudar o contrato, a tela falha
 * com uma mensagem clara em vez de mostrar dado quebrado.
 */
export async function buscarJson<T extends z.ZodType>(caminho: string, schema: T, sinal?: AbortSignal): Promise<z.infer<T>> {
  const resposta = await fetch(caminho, { headers: { Accept: 'application/json' }, signal: sinal, credentials: 'same-origin' })
  if (!resposta.ok) return lerErro(resposta)
  return schema.parse(await resposta.json())
}

/** POST (ou outro método) com o cabeçalho anti-CSRF. Sem schema, ignora o corpo da resposta (204). */
export async function enviar<T extends z.ZodType>(
  caminho: string,
  corpo?: unknown,
  schema?: T,
  cabecalhos: Record<string, string> = {},
  metodo: 'POST' | 'PATCH' | 'PUT' | 'DELETE' = 'POST',
): Promise<z.infer<T>> {
  const resposta = await fetch(caminho, {
    method: metodo,
    credentials: 'same-origin',
    headers: {
      Accept: 'application/json',
      ...cabecalhoAntiCsrf,
      ...cabecalhos,
      ...(corpo === undefined ? {} : { 'Content-Type': 'application/json' }),
    },
    body: corpo === undefined ? undefined : JSON.stringify(corpo),
  })
  if (!resposta.ok) return lerErro(resposta)
  return schema ? schema.parse(await resposta.json()) : (undefined as z.infer<T>)
}

/** POST de arquivo (multipart). O navegador monta o Content-Type com a fronteira do formulário. */
export async function enviarArquivo<T extends z.ZodType>(
  caminho: string,
  arquivo: Blob,
  schema: T,
  campo = 'arquivo',
  nome = arquivo instanceof File ? arquivo.name : 'arquivo',
): Promise<z.infer<T>> {
  const formulario = new FormData()
  formulario.append(campo, arquivo, nome)
  const resposta = await fetch(caminho, {
    method: 'POST',
    credentials: 'same-origin',
    headers: { Accept: 'application/json', ...cabecalhoAntiCsrf },
    body: formulario,
  })
  if (!resposta.ok) return lerErro(resposta)
  return schema.parse(await resposta.json())
}
