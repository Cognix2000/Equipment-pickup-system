/**
 * Captura o evento `beforeinstallprompt` do navegador e detecta o fluxo
 * de instalação no iOS (que não dispara esse evento — usa o menu de
 * compartilhamento do Safari).
 *
 * Uso:
 *   const { podeMostrar, ehIos, instalar } = useInstalacaoPwa()
 *   // podeMostrar === true → mostrar o banner
 *   // ehIos === true       → mostrar tutorial "Adicionar à Tela de Início"
 */

import { useEffect, useState } from 'react'

type EventoDeInstalacao = Event & { prompt(): Promise<void>; userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }> }

/** Chave do localStorage: se 'sim', o usuário já dispensou o banner. */
const CHAVE_DISPENSADO = 'pwa-banner-dispensado'

function jaFoiDispensado() {
  try {
    return localStorage.getItem(CHAVE_DISPENSADO) === 'sim'
  } catch {
    return false
  }
}

function marcarComoDispensado() {
  try {
    localStorage.setItem(CHAVE_DISPENSADO, 'sim')
  } catch {
    // localStorage bloqueado (ex.: modo privado) — sem problema
  }
}

/** Detecta iOS/iPadOS para o fluxo alternativo de instalação. */
function detectarIos() {
  const ua = navigator.userAgent
  const ehIosNativo = /iphone|ipad|ipod/i.test(ua)
  // iPad com iPadOS 13+ pode reportar como Mac — verificamos o toque
  const ehIpadModerno = navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1
  return (ehIosNativo || ehIpadModerno) && !(window as Window & { MSStream?: unknown }).MSStream
}

/** Verifica se já está instalado como PWA (standalone). */
function jaEstaInstalado() {
  return window.matchMedia('(display-mode: standalone)').matches || (navigator as Navigator & { standalone?: boolean }).standalone === true
}

export type ResultadoDeInstalacao = {
  /** Exibir o banner Android/Chrome (evento disponível, não dispensado, não instalado). */
  podeMostrar: boolean
  /** Exibir o tutorial iOS (Safari, não instalado, não dispensado). */
  ehIos: boolean
  /** Chama o prompt nativo de instalação (só funciona em Android/Chrome). */
  instalar: () => Promise<void>
  /** Oculta o banner e persiste a decisão por 90 dias. */
  dispensar: () => void
}

export function useInstalacaoPwa(): ResultadoDeInstalacao {
  const [evento, setEvento] = useState<EventoDeInstalacao | null>(null)
  const [dispensado, setDispensado] = useState(jaFoiDispensado)
  const [instalado, setInstalado] = useState(jaEstaInstalado)

  useEffect(() => {
    if (jaEstaInstalado()) return

    const aoCapturar = (e: Event) => {
      e.preventDefault()
      setEvento(e as EventoDeInstalacao)
    }

    window.addEventListener('beforeinstallprompt', aoCapturar)

    // Detecta instalação em tempo real
    const mq = window.matchMedia('(display-mode: standalone)')
    const aoInstalar = (e: MediaQueryListEvent) => { if (e.matches) setInstalado(true) }
    mq.addEventListener('change', aoInstalar)

    return () => {
      window.removeEventListener('beforeinstallprompt', aoCapturar)
      mq.removeEventListener('change', aoInstalar)
    }
  }, [])

  const instalar = async () => {
    if (!evento) return
    await evento.prompt()
    const { outcome } = await evento.userChoice
    if (outcome === 'accepted') {
      setInstalado(true)
    }
    setEvento(null)
  }

  const dispensar = () => {
    marcarComoDispensado()
    setDispensado(true)
    setEvento(null)
  }

  const ios = detectarIos()
  const podeMostrar = !instalado && !dispensado && evento !== null
  const ehIos = !instalado && !dispensado && ios

  return { podeMostrar, ehIos, instalar, dispensar }
}
