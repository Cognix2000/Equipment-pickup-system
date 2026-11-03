import { useQuery } from '@tanstack/react-query'
import { Marker } from 'maplibre-gl'
import { useEffect, useRef } from 'react'
import { criarMapa, enquadrar } from './baseDoMapa'
import { configuracaoDoMapaQuery, type TecnicoNoMapa } from './localizacao'

type Props = {
  tecnicos: (TecnicoNoMapa & { latitude: number; longitude: number; recente: boolean })[]
  aoEscolher: (id: string) => void
  className?: string
}

const iniciais = (nome: string) =>
  nome
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((parte) => parte[0]!.toUpperCase())
    .join('')

/** Mapa do gestor: um pino por técnico com posição. Quem não manda posição há mais de 15 min fica apagado. */
export default function MapaDaEquipe({ tecnicos, aoEscolher, className }: Props) {
  const elemento = useRef<HTMLDivElement>(null)
  const { data: configuracao } = useQuery(configuracaoDoMapaQuery)
  const estilo = configuracao?.estilo

  // Recriar o mapa a cada atualização (15 s) faria o gestor perder o zoom: o mapa nasce uma vez por estilo,
  // e só os pinos mudam.
  const mapa = useRef<ReturnType<typeof criarMapa> | null>(null)
  const enquadrado = useRef(false)

  useEffect(() => {
    if (!elemento.current || !estilo) return
    mapa.current = criarMapa(elemento.current, estilo)
    enquadrado.current = false
    return () => {
      mapa.current?.remove()
      mapa.current = null
    }
  }, [estilo])

  useEffect(() => {
    const atual = mapa.current
    if (!atual) return
    const marcadores = tecnicos.map((t) => {
      const pino = document.createElement('button')
      pino.type = 'button'
      pino.textContent = iniciais(t.nome)
      pino.setAttribute('aria-label', `${t.nome}: ver na lista`)
      pino.className =
        'grid size-10 place-items-center rounded-full border-[3px] border-white text-sm font-semibold shadow-md ' +
        (t.recente ? 'bg-azul text-white' : 'bg-desabilitado text-marinho')
      pino.addEventListener('click', () => aoEscolher(t.id))
      return new Marker({ element: pino }).setLngLat([t.longitude, t.latitude]).addTo(atual)
    })
    if (!enquadrado.current && tecnicos.length > 0) {
      enquadrar(
        atual,
        tecnicos.map((t): [number, number] => [t.longitude, t.latitude]),
      )
      enquadrado.current = true
    }
    return () => marcadores.forEach((m) => m.remove())
  }, [tecnicos, aoEscolher, estilo])

  return <div ref={elemento} className={`bg-mapa ${className ?? ''}`} role="region" aria-label="Mapa da equipe" />
}
