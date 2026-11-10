import { queryOptions } from '@tanstack/react-query'
import { z } from 'zod'
import { buscarJson, enviar } from '@/lib/api'

/**
 * Resgate por PIX do técnico terceiro (ADR-014). O CLT recebe na folha e só acompanha. O dinheiro sai para a
 * chave PIX que o financeiro aprovou, e quem confirma que caiu é o gateway (Asaas), com o ID de Fim a Fim.
 */

const dataHora = z.iso.datetime({ offset: true })

export const vinculo = z.enum(['clt', 'terceiro'])
export type Vinculo = z.infer<typeof vinculo>
export const rotuloDoVinculo: Record<Vinculo, string> = { clt: 'CLT', terceiro: 'Terceiro' }

export const tipoDeChave = z.enum(['cpf', 'cnpj', 'email', 'telefone', 'aleatoria'])
export type TipoDeChave = z.infer<typeof tipoDeChave>
export const rotuloDoTipoDeChave: Record<TipoDeChave, string> = {
  cpf: 'CPF',
  cnpj: 'CNPJ',
  email: 'E-mail',
  telefone: 'Celular',
  aleatoria: 'Chave aleatória',
}

const situacaoDoResgate = z.enum(['solicitado', 'enviado', 'pago', 'falhou'])
export type SituacaoDoResgate = z.infer<typeof situacaoDoResgate>

export const chaveDoTecnico = z.object({
  id: z.uuid(),
  tipo: tipoDeChave,
  mascarada: z.string(),
  situacao: z.enum(['aprovada', 'pendente', 'recusada']),
  cadastradaEm: dataHora,
  motivoRecusa: z.string().nullable(),
})

export const resgateFeito = z.object({
  id: z.uuid(),
  valorCentavos: z.number().int(),
  quantidade: z.number().int(),
  situacao: situacaoDoResgate,
  solicitadoEm: dataHora,
  concluidoEm: dataHora.nullable(),
  idFimAFim: z.string().nullable(),
  comprovanteUrl: z.string().nullable(),
  motivoFalha: z.string().nullable(),
})
export type ResgateFeito = z.infer<typeof resgateFeito>

export const meuResgate = z.object({
  vinculo,
  resgateLigado: z.boolean(),
  minimoCentavos: z.number().int(),
  disponivel: z.object({ quantidade: z.number().int(), valorCentavos: z.number().int() }),
  chaves: z.array(chaveDoTecnico),
  resgates: z.array(resgateFeito),
})
export type MeuResgate = z.infer<typeof meuResgate>

export const meuResgateQuery = queryOptions({
  queryKey: ['repasses', 'resgate', 'meu'],
  queryFn: ({ signal }) => buscarJson('/api/resgates/meu', meuResgate, signal),
  // Enquanto um PIX está a caminho, confere de 5 em 5 segundos até o aviso do gateway chegar.
  refetchInterval: (consulta) =>
    consulta.state.data?.resgates.some((r) => r.situacao === 'solicitado' || r.situacao === 'enviado') ? 5_000 : false,
})

export const cadastrarChavePix = (tipo: TipoDeChave, chave: string) => enviar('/api/resgates/minha-chave-pix', { tipo, chave }, chaveDoTecnico)
export const solicitarResgate = () => enviar('/api/resgates', undefined, meuResgate)

// Financeiro e gestor
export const chaveParaAprovar = z.object({
  id: z.uuid(),
  tecnicoId: z.uuid(),
  tecnicoNome: z.string(),
  tipo: tipoDeChave,
  chave: z.string(),
  cadastradaEm: dataHora,
  trocaDeChave: z.boolean(),
})
export type ChaveParaAprovar = z.infer<typeof chaveParaAprovar>

export const chavesParaAprovarQuery = queryOptions({
  queryKey: ['repasses', 'resgate', 'chaves'],
  queryFn: ({ signal }) => buscarJson('/api/resgates/chaves-pix', z.array(chaveParaAprovar), signal),
})
export const aprovarChave = (id: string) => enviar(`/api/resgates/chaves-pix/${id}/aprovar`)
export const recusarChave = (id: string, motivo: string) => enviar(`/api/resgates/chaves-pix/${id}/recusar`, { motivo: motivo || undefined })

export const resgateNaLista = resgateFeito.extend({ tecnicoId: z.uuid(), tecnicoNome: z.string() })
export const resgatesQuery = queryOptions({
  queryKey: ['repasses', 'resgate', 'todos'],
  queryFn: ({ signal }) => buscarJson('/api/resgates', z.array(resgateNaLista), signal),
})

export const folhaDoMes = z.object({
  mes: z.string(),
  totalCentavos: z.number().int(),
  tecnicos: z.array(
    z.object({ id: z.uuid(), nome: z.string(), situacao: z.string(), quantidade: z.number().int(), valorCentavos: z.number().int(), emAbertoCentavos: z.number().int() }),
  ),
})
export const folhaDoMesQuery = (mes: string) =>
  queryOptions({
    queryKey: ['repasses', 'folha', mes],
    queryFn: ({ signal }) => buscarJson(`/api/repasses/folha?mes=${encodeURIComponent(mes)}`, folhaDoMes, signal),
  })

/** Como o técnico lê a situação do resgate. */
export const textoDaSituacao: Record<SituacaoDoResgate, string> = {
  solicitado: 'A caminho',
  enviado: 'A caminho',
  pago: 'Pago',
  falhou: 'Não saiu',
}

/** "2026-11" do mês de uma data, no horário de Brasília. */
export function mesDe(data: Date): string {
  const partes = new Intl.DateTimeFormat('en-CA', { year: 'numeric', month: '2-digit', timeZone: 'America/Sao_Paulo' }).formatToParts(data)
  return `${partes.find((p) => p.type === 'year')!.value}-${partes.find((p) => p.type === 'month')!.value}`
}
