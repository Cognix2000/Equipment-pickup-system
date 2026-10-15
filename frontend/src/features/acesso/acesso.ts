import { queryOptions, useQuery } from '@tanstack/react-query'
import { z } from 'zod'
import { ErroDaApi, buscarJson, enviar } from '@/lib/api'

// Contrato do módulo Acesso (ADR-010).
export const papel = z.enum(['admin', 'financeiro', 'tecnico', 'parceiro', 'estoque'])
export type Papel = z.infer<typeof papel>

const eu = z.object({
  id: z.uuid(),
  nome: z.string(),
  papel,
  etapa: z.enum(['completo', 'codigo_2fa', 'configurar_2fa']),
})
export type Eu = z.infer<typeof eu>

export const respostaDeLogin = z.object({
  etapa: z.enum(['pronto', 'codigo', 'configurar']),
  usuario: z.object({ id: z.uuid(), nome: z.string(), email: z.string(), papel }).nullable(),
})

export const qrDoSegundoFator = z.object({ enderecoOtpAuth: z.string(), segredo: z.string() })

export const usuarioNaLista = z.object({
  id: z.uuid(),
  nome: z.string(),
  email: z.string(),
  papel,
  situacao: z.enum(['pendente', 'ativo', 'suspenso', 'revogado']),
  doisFatores: z.boolean(),
  ultimoLoginEm: z.iso.datetime({ offset: true }).nullable(),
})
export type UsuarioNaLista = z.infer<typeof usuarioNaLista>

/** Usuários do sistema. Só o gestor lista (RF02). */
export const usuariosQuery = queryOptions({
  queryKey: ['usuarios'],
  queryFn: ({ signal }) => buscarJson('/api/usuarios', z.array(usuarioNaLista), signal),
})

export const linkDeConvite = z.object({ caminho: z.string(), expiraEm: z.iso.datetime({ offset: true }) })
export type LinkDeConvite = z.infer<typeof linkDeConvite>

export const rotuloDoPapel: Record<Papel, string> = {
  admin: 'Gestor',
  financeiro: 'Financeiro',
  tecnico: 'Técnico',
  parceiro: 'Parceiro',
  estoque: 'Estoque',
}

/** Sessão atual. 401 não é erro aqui: só quer dizer que ninguém entrou. */
export const euQuery = queryOptions({
  queryKey: ['acesso', 'eu'],
  queryFn: async ({ signal }) => {
    try {
      return await buscarJson('/api/acesso/eu', eu, signal)
    } catch (erro) {
      if (erro instanceof ErroDaApi && erro.status === 401) return null
      throw erro
    }
  },
  staleTime: 60_000,
  retry: false,
})

export const useEu = () => useQuery(euQuery)

/** Tela inicial de cada papel. */
export function inicioDo(papel: Papel): string {
  if (papel === 'tecnico') return '/tecnico/ordens'
  if (papel === 'estoque') return '/estoque/conferir'
  return '/financeiro/ordens'
}

export const acesso = {
  entrar: (email: string, senha: string) => enviar('/api/acesso/entrar', { email, senha }, respostaDeLogin),
  confirmarCodigo: (codigo: string) => enviar('/api/acesso/codigo', { codigo }, respostaDeLogin),
  iniciar2FA: () => enviar('/api/acesso/2fa/iniciar', undefined, qrDoSegundoFator),
  confirmar2FA: (codigo: string) => enviar('/api/acesso/2fa/confirmar', { codigo }, respostaDeLogin),
  aceitarConvite: (token: string, senha: string) => enviar('/api/acesso/convite', { token, senha }),
  sair: () => enviar('/api/acesso/sair'),
}
