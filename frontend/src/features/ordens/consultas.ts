import { queryOptions, useQuery } from '@tanstack/react-query'
import { z } from 'zod'
import { buscarJson } from '@/lib/api'
import { listaDeOrdens, ordem, type StatusOrdem } from './tipos'

export type FiltroDeOrdens = { status?: StatusOrdem; bairro?: string; busca?: string }

export const ordensQuery = (filtro: FiltroDeOrdens = {}) =>
  queryOptions({
    queryKey: ['ordens', filtro],
    queryFn: ({ signal }) => {
      const busca = new URLSearchParams()
      if (filtro.status) busca.set('status', filtro.status)
      if (filtro.bairro) busca.set('bairro', filtro.bairro)
      if (filtro.busca) busca.set('busca', filtro.busca)
      const sufixo = busca.size > 0 ? `?${busca}` : ''
      return buscarJson(`/api/ordens${sufixo}`, listaDeOrdens, signal)
    },
  })

export const ordemQuery = (id: string) =>
  queryOptions({
    queryKey: ['ordens', 'detalhe', id],
    queryFn: ({ signal }) => buscarJson(`/api/ordens/${id}`, ordem, signal),
  })

export const useOrdens = (filtro?: FiltroDeOrdens) => useQuery(ordensQuery(filtro))
export const useOrdem = (id: string) => useQuery(ordemQuery(id))

const contagem = z.object({ retiradas: z.number().int(), conferidas: z.number().int(), visitasSemSucesso: z.number().int() })

export const resumoDoTecnico = z.object({
  pendentes: z.number().int(),
  aguardandoConferencia: z.number().int(),
  hoje: contagem,
  semana: contagem,
  mes: contagem,
})
export type ResumoDoTecnico = z.infer<typeof resumoDoTecnico>

/** Controle do técnico: o que ele já recolheu e o que o estoque já conferiu. Atualiza junto com as ordens. */
export const resumoDoTecnicoQuery = queryOptions({
  queryKey: ['ordens', 'meu-resumo'],
  queryFn: ({ signal }) => buscarJson('/api/ordens/meu-resumo', resumoDoTecnico, signal),
})
