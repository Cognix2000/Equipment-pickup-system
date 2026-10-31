import { queryOptions } from '@tanstack/react-query'
import { z } from 'zod'
import type { Gps } from '@/features/ordens/comandos'
import { buscarJson, enviar } from '@/lib/api'

/**
 * Localização do técnico em campo. O app manda só a última posição, e só com o app aberto na tela;
 * quem vê a equipe é o gestor. Estoque e financeiro não têm acesso (a API recusa).
 */

const dataHora = z.iso.datetime({ offset: true })

/** "real" = ruas de verdade (produção); "simulado" = a cidade fictícia dos dados de homologação. */
export const configuracaoDoMapa = z.object({ estilo: z.enum(['real', 'simulado']) })
export type EstiloDoMapa = z.infer<typeof configuracaoDoMapa>['estilo']

export const configuracaoDoMapaQuery = queryOptions({
  queryKey: ['mapa', 'configuracao'],
  queryFn: ({ signal }) => buscarJson('/api/mapa', configuracaoDoMapa, signal),
  staleTime: Infinity, // muda só com nova versão do servidor
})

export const tecnicoNoMapa = z.object({
  id: z.uuid(),
  nome: z.string(),
  latitude: z.number().nullable(),
  longitude: z.number().nullable(),
  precisaoM: z.number().nullable(),
  registradaEm: dataHora.nullable(),
  ordensComEle: z.number().int(),
})
export type TecnicoNoMapa = z.infer<typeof tecnicoNoMapa>

export const equipeQuery = queryOptions({
  queryKey: ['localizacao', 'equipe'],
  queryFn: ({ signal }) => buscarJson('/api/localizacao/equipe', z.array(tecnicoNoMapa), signal),
  refetchInterval: 15_000, // o gestor acompanha sem recarregar a página
})

export const enviarMinhaPosicao = (gps: Gps, registradaEm: Date) =>
  enviar('/api/localizacao/minha', { ...gps, registradaEm: registradaEm.toISOString() })

type Ponto = { latitude: number; longitude: number }

/** Distância em linha reta (haversine), em metros. */
export function distanciaEmMetros(a: Ponto, b: Ponto): number {
  const raio = 6_371_000
  const rad = (graus: number) => (graus * Math.PI) / 180
  const dLat = rad(b.latitude - a.latitude)
  const dLon = rad(b.longitude - a.longitude)
  const h = Math.sin(dLat / 2) ** 2 + Math.cos(rad(a.latitude)) * Math.cos(rad(b.latitude)) * Math.sin(dLon / 2) ** 2
  return 2 * raio * Math.asin(Math.sqrt(h))
}

export const IntervaloMinimoMs = 10_000
export const IntervaloNormalMs = 30_000
export const DeslocamentoM = 75

/**
 * Economiza bateria e dados: manda a cada 30 s, ou antes disso se o técnico andou 75 m,
 * mas nunca mais de uma vez a cada 10 s (de carro, o GPS atualiza o tempo todo).
 */
export function deveEnviar(ultimo: (Ponto & { em: number }) | null, atual: Ponto, agora: number): boolean {
  if (!ultimo) return true
  const passou = agora - ultimo.em
  if (passou < IntervaloMinimoMs) return false
  return passou >= IntervaloNormalMs || distanciaEmMetros(ultimo, atual) >= DeslocamentoM
}

const formatoDia = new Intl.DateTimeFormat('pt-BR', { day: '2-digit', month: '2-digit', timeZone: 'America/Sao_Paulo' })

/** Há quanto tempo o técnico mandou a posição, e se ela ainda vale como "em campo agora" (até 15 min). */
export function descreverSinal(registradaEm: string | null, agora: number): { texto: string; recente: boolean } {
  if (!registradaEm) return { texto: 'Ainda não abriu o app', recente: false }
  const minutos = Math.max(0, Math.floor((agora - new Date(registradaEm).getTime()) / 60_000))
  if (minutos < 2) return { texto: 'Agora', recente: true }
  if (minutos < 60) return { texto: `Há ${minutos} min`, recente: minutos <= 15 }
  if (minutos < 24 * 60) return { texto: `Há ${Math.floor(minutos / 60)} h`, recente: false }
  return { texto: `Em ${formatoDia.format(new Date(registradaEm))}`, recente: false }
}
