import { useQuery } from '@tanstack/react-query'
import { Marker, type Map as MapaMapLibre } from 'maplibre-gl'
import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router'
import type { Ordem } from '@/features/ordens/tipos'
import { criarMapa, desabilitarEdificios3D, enquadrar, habilitarEdificios3D, pontoDaMinhaPosicao, reabilitarEdificios3D } from './baseDoMapa'
import { configuracaoDoMapaQuery } from './localizacao'
import { useMinhaPosicao } from './minhaPosicao'

type Props = { ordens: Ordem[]; className?: string }

// Preferência fica por sessão: no celular com rede fraca, o técnico que escolheu 2D não quer o mapa
// reabrir em 3D no próximo passo pela cidade. Imersivo 3D (nov/2026, Opção 2): quem nunca escolheu
// nada abre em 3D — é a vibe padrão do app agora. Só explícito 'false' força 2D.
const ChaveModo3D = 'mapa:modo3d'

function modo3dInicial(): boolean {
  try {
    return localStorage.getItem(ChaveModo3D) !== 'false'
  } catch {
    return true
  }
}

/**
 * Pino de ordem em formato de gota, vermelho-laranja vívido como no comparativo visual (Opção 2).
 * Usa SVG inline (acessível por `aria-label`) em vez de classes para garantir o visual igual em
 * dark mode e em viewports antigos — o fundo do mapa pode ser bege ou dark, mas o pino é sempre o mesmo.
 */
function pinoDeOrdem(numero: number, destacado: boolean): HTMLElement {
  const cor = destacado ? '#ff3a00' : '#ff6b1a'
  const corNumero = destacado ? '#8a1b00' : '#a53500'
  const pino = document.createElement('button')
  pino.type = 'button'
  pino.className = 'block cursor-pointer bg-transparent border-0 p-0'
  pino.innerHTML =
    `<svg width="34" height="42" viewBox="0 0 34 42" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">` +
    `<path d="M17 1 C 8 1 2 8 2 16 C 2 26 17 40 17 40 C 17 40 32 26 32 16 C 32 8 26 1 17 1 Z" ` +
    `fill="${cor}" stroke="#ffffff" stroke-width="2.5"/>` +
    `<circle cx="17" cy="16" r="7" fill="#ffffff"/>` +
    `<text x="17" y="20" text-anchor="middle" font-family="Inter,sans-serif" font-size="11" font-weight="700" ` +
    `fill="${corNumero}">${numero}</text>` +
    `</svg>`
  return pino
}

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
  const [modo3D, definirModo3D] = useState<boolean>(modo3dInicial)

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
    habilitarEdificios3D(novo, estilo)
    if (modo3D && estilo === 'real') novo.once('load', () => novo.easeTo({ pitch: 55, duration: 600 }))
    const comPonto = ordens.filter((o) => o.localizacao)

    const marcadores = comPonto.map((o, i) => {
      const pino = pinoDeOrdem(i + 1, i === 0)
      pino.setAttribute('aria-label', `Abrir ordem ${i + 1}: ${o.endereco.logradouro}, ${o.endereco.numero ?? 's/ nº'}`)
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
    // modo3D fica fora: a troca não reinicia o mapa, só muda pitch e visibilidade dos prédios.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ordens, navegar, estilo])

  // Alterna 2D/3D sem recriar o mapa: anima pitch e muda a camada de extrusão.
  function alternarModo3D() {
    const atual = mapa.current
    if (!atual) return
    const novo = !modo3D
    definirModo3D(novo)
    try {
      localStorage.setItem(ChaveModo3D, String(novo))
    } catch {
      // Privado ou sem permissão: segue sem lembrar; não impede a troca.
    }
    if (novo) {
      reabilitarEdificios3D(atual)
      atual.easeTo({ pitch: 55, duration: 500 })
    } else {
      desabilitarEdificios3D(atual)
      atual.easeTo({ pitch: 0, bearing: 0, duration: 500 })
    }
  }

  return (
    <div className={`relative ${className ?? ''}`}>
      <div ref={elemento} className="h-full w-full bg-mapa" role="region" aria-label="Mapa das ordens" />
      {estilo === 'real' && (
        <button
          type="button"
          aria-pressed={modo3D}
          onClick={alternarModo3D}
          className="absolute top-2 right-2 z-10 min-h-9 rounded-full border border-borda-forte bg-superficie/95 px-3 text-sm font-semibold text-marinho shadow-md backdrop-blur hover:bg-superficie"
          title={modo3D ? 'Voltar para o mapa 2D' : 'Ver a cidade em 3D'}
        >
          {modo3D ? '2D' : '3D'}
        </button>
      )}
    </div>
  )
}
