import { queryOptions } from '@tanstack/react-query'
import { z } from 'zod'
import { buscarJson, enviar } from '@/lib/api'

/**
 * Repasse ao técnico (CLT ou PJ, o mesmo valor): cada retirada conferida pelo estoque vira um crédito, e o
 * financeiro registra o pagamento do que está em aberto. Os valores vêm em centavos, para não arredondar errado.
 */

const dataHora = z.iso.datetime({ offset: true })

export const meuRepasse = z.object({
  valorPorRetiradaCentavos: z.number().int(),
  aReceber: z.object({ quantidade: z.number().int(), valorCentavos: z.number().int() }),
  ultimosPagamentos: z.array(z.object({ pagoEm: dataHora, valorCentavos: z.number().int(), quantidade: z.number().int() })),
})

export const tecnicoNaLista = z.object({
  id: z.uuid(),
  nome: z.string(),
  situacao: z.string(),
  quantidade: z.number().int(),
  valorCentavos: z.number().int(),
  ultimoPagamentoEm: dataHora.nullable(),
})
export type TecnicoNaLista = z.infer<typeof tecnicoNaLista>

export const detalheDoRepasse = z.object({
  id: z.uuid(),
  nome: z.string(),
  quantidade: z.number().int(),
  valorCentavos: z.number().int(),
  emAberto: z.array(
    z.object({ id: z.uuid(), creditadoEm: dataHora, valorCentavos: z.number().int(), numeroOs: z.number().int().nullable(), bairro: z.string().nullable() }),
  ),
  pagamentos: z.array(
    z.object({ id: z.uuid(), pagoEm: dataHora, valorCentavos: z.number().int(), quantidade: z.number().int(), observacao: z.string().nullable() }),
  ),
})

export const meuRepasseQuery = queryOptions({
  queryKey: ['repasses', 'meu'],
  queryFn: ({ signal }) => buscarJson('/api/repasses/meu', meuRepasse, signal),
})

export const repassesQuery = queryOptions({
  queryKey: ['repasses', 'tecnicos'],
  queryFn: ({ signal }) => buscarJson('/api/repasses', z.array(tecnicoNaLista), signal),
})

export const detalheDoRepasseQuery = (tecnicoId: string) =>
  queryOptions({
    queryKey: ['repasses', 'tecnicos', tecnicoId],
    queryFn: ({ signal }) => buscarJson(`/api/repasses/${tecnicoId}`, detalheDoRepasse, signal),
  })

/** O valor confirmado é o que o financeiro viu: se entrou retirada nova nesse meio-tempo, a API recusa. */
export const registrarPagamento = (tecnicoId: string, valorCentavos: number, observacao?: string) =>
  enviar(`/api/repasses/${tecnicoId}/pagamentos`, { valorCentavos, observacao: observacao || undefined })

const formatoReais = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' })

/** 1050 → "R$ 10,50". */
export const emReais = (centavos: number) => formatoReais.format(centavos / 100)
