import { useQuery } from '@tanstack/react-query'
import { Marker, type Map as MapaMapLibre } from 'maplibre-gl'
import { useEffect, useRef } from 'react'
import { useNavigate } from 'react-router'
import type { Ordem } from '@/features/ordens/tipos'
import { criarMapa, enquadrar, pontoDaMinhaPosicao } from './baseDoMapa'
import { configuracaoDoMapaQuery } from './localizacao'
import { useMinhaPosicao } from './minhaPosicao'

type Props = { ordens: Ordem[]; className?: string }

/**
 * Mapa do técnico: as ordens dele numeradas e o ponto azul da posição atual.
 * A planilha do HubSoft não traz coordenada: a ordem ganha o pino no primeiro registro com GPS (retirada ou visita).
 */
export default function MapaDeOrdens({ ordens, className }: Props) {
  const elemento = useRef<HTMLDivElement>(null)
  const mapa = useRef<MapaMapLibre | null>(null)
  const ponto = useRef<Marker | null>(null)
  const navegar = useNavigate()
  const { data: configuracao } = useQuery(configuracaoDoMapaQuery)
  const { posicao } = useMinhaPosicao()
  const estilo = configuracao?.estilo

  // Lida no momento de enquadrar, sem recriar o mapa a cada passo do técnico.
  const ultimaPosicao = useRef(posicao)
  useEffect(() => {
    ultimaPosicao.current = posicao
    if (!mapa.current) return
    if (!posicao) {
      ponto.current?.remove()
      ponto.current = null
    } else if (ponto.current) ponto.current.setLngLat([posicao.longitude, posicao.latitude])
    else ponto.current = pontoDaMinhaPosicao(posicao).addTo(mapa.current)
  }, [posicao])

  useEffect(() => {
    if (!elemento.current || !estilo) return
    const novo = criarMapa(elemento.current, estilo)
    mapa.current = novo
    const comPonto = ordens.filter((o) => o.localizacao)

    const marcadores = comPonto.map((o, i) => {
      const pino = document.createElement('button')
      pino.type = 'button'
      pino.textContent = String(i + 1)
      pino.setAttribute('aria-label', `Abrir ordem ${i + 1}: ${o.endereco.logradouro}, ${o.endereco.numero ?? 's/ nº'}`)
      pino.className =
        'grid size-9 place-items-center rounded-full border-[3px] border-white text-sm font-semibold text-white shadow-md ' +
        (i === 0 ? 'bg-azul' : 'bg-marinho')
      pino.addEventListener('click', () => navegar(`/tecnico/ordens/${o.id}`))
      return new Marker({ element: pino }).setLngLat([o.localizacao!.longitude, o.localizacao!.latitude]).addTo(novo)
    })

    const voce = ultimaPosicao.current
    if (voce) ponto.current = pontoDaMinhaPosicao(voce).addTo(novo)
    const pontos = comPonto.map((o): [number, number] => [o.localizacao!.longitude, o.localizacao!.latitude])
    // Sem ordem com pino (comum em produção), o mapa abre onde o técnico está.
    enquadrar(novo, pontos.length > 0 ? pontos : voce ? [[voce.longitude, voce.latitude]] : [])

    return () => {
      marcadores.forEach((m) => m.remove())
      ponto.current?.remove()
      ponto.current = null
      novo.remove()
      mapa.current = null
    }
  }, [ordens, navegar, estilo])

  return <div ref={elemento} className={`bg-mapa ${className ?? ''}`} role="region" aria-label="Mapa das ordens" />
}
