import { useEffect, useSyncExternalStore } from 'react'
import type { Gps } from '@/features/ordens/comandos'
import { deveEnviar, enviarMinhaPosicao } from './localizacao'

/**
 * Compartilha a posição do técnico enquanto o app está aberto na tela (decisão do Pedro: só a última posição,
 * sem trajeto). Com a aba escondida ou o celular bloqueado, para; ao voltar, retoma.
 * O estado fica num store simples para o mapa e o aviso lerem sem passar props pelo layout.
 */

export type SituacaoDaPosicao = 'desligada' | 'procurando' | 'compartilhando' | 'negada' | 'indisponivel'
type Estado = { situacao: SituacaoDaPosicao; posicao: Gps | null }

let estado: Estado = { situacao: 'desligada', posicao: null }
const ouvintes = new Set<() => void>()

function definir(novo: Partial<Estado>) {
  estado = { ...estado, ...novo }
  ouvintes.forEach((avisar) => avisar())
}

function assinar(avisar: () => void) {
  ouvintes.add(avisar)
  return () => ouvintes.delete(avisar)
}

export const useMinhaPosicao = () => useSyncExternalStore(assinar, () => estado)

/** Confirma a posição a cada minuto, mesmo parado: o gestor sabe que o app continua aberto. */
const ConfirmarACadaMs = 60_000

export function useCompartilharPosicao(ativo: boolean) {
  useEffect(() => {
    if (!ativo) return
    if (!('geolocation' in navigator)) {
      definir({ situacao: 'indisponivel' })
      return () => definir({ situacao: 'desligada', posicao: null })
    }

    let ultimoEnvio: { latitude: number; longitude: number; em: number } | null = null
    let observacao: number | null = null
    let confirmacao: ReturnType<typeof setInterval> | null = null
    const opcoes: PositionOptions = { enableHighAccuracy: true, maximumAge: 15_000, timeout: 30_000 }

    const recebeu = ({ coords }: GeolocationPosition) => {
      const gps: Gps = {
        latitude: coords.latitude,
        longitude: coords.longitude,
        precisaoM: Number.isFinite(coords.accuracy) ? Math.round(coords.accuracy) : null,
      }
      definir({ situacao: 'compartilhando', posicao: gps })
      const agora = Date.now()
      if (!deveEnviar(ultimoEnvio, gps, agora)) return
      ultimoEnvio = { latitude: gps.latitude, longitude: gps.longitude, em: agora }
      // Sem sinal, a próxima leitura tenta de novo; posição velha não vale a pena guardar para depois.
      enviarMinhaPosicao(gps, new Date(agora)).catch(() => undefined)
    }
    const falhou = (erro: GeolocationPositionError) => {
      if (erro.code === erro.PERMISSION_DENIED) definir({ situacao: 'negada', posicao: null })
    }

    const comecar = () => {
      if (observacao !== null) return
      if (estado.situacao !== 'compartilhando') definir({ situacao: 'procurando' })
      observacao = navigator.geolocation.watchPosition(recebeu, falhou, opcoes)
      confirmacao = setInterval(() => navigator.geolocation.getCurrentPosition(recebeu, falhou, opcoes), ConfirmarACadaMs)
    }
    const parar = () => {
      if (observacao !== null) navigator.geolocation.clearWatch(observacao)
      if (confirmacao !== null) clearInterval(confirmacao)
      observacao = null
      confirmacao = null
    }
    const aoMudarVisibilidade = () => (document.visibilityState === 'visible' ? comecar() : parar())

    if (document.visibilityState === 'visible') comecar()
    document.addEventListener('visibilitychange', aoMudarVisibilidade)
    return () => {
      document.removeEventListener('visibilitychange', aoMudarVisibilidade)
      parar()
      definir({ situacao: 'desligada', posicao: null })
    }
  }, [ativo])
}
