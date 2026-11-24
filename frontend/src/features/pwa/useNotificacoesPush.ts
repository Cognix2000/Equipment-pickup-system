/**
 * Gerencia a inscrição de Web Push (VAPID) do dispositivo.
 *
 * Fluxo:
 *   1. O frontend consulta GET /api/acesso/push/chave-publica.
 *   2. Se o servidor devolver a chave, habilita o botão "Ativar notificações".
 *   3. Ao ativar: pede permissão ao navegador → chama pushManager.subscribe() →
 *      faz POST /api/acesso/push/inscricao com as chaves.
 *   4. Ao desativar: cancela a assinatura no navegador e DELETE /api/acesso/push/inscricao.
 */

import { useEffect, useState } from 'react'
import { enviar } from '@/lib/api'
import { z } from 'zod'

export type EstadoDoPush = 'nao-suportado' | 'nao-configurado' | 'negado' | 'inativo' | 'ativo' | 'carregando'

const ChavePublicaSchema = z.object({ chavePublica: z.string() })

/** Converte uma chave VAPID base64url para Uint8Array (exigido pelo pushManager.subscribe). */
function base64UrlParaUint8Array(base64Url: string): Uint8Array<ArrayBuffer> {
  const padding = '='.repeat((4 - (base64Url.length % 4)) % 4)
  const base64 = (base64Url + padding).replace(/-/g, '+').replace(/_/g, '/')
  const raw = atob(base64)
  const buffer = new ArrayBuffer(raw.length)
  const view = new Uint8Array(buffer)
  for (let i = 0; i < raw.length; i++) view[i] = raw.charCodeAt(i)
  return view
}

/** Monta o corpo que o backend espera no POST /push/inscricao. */
function inscricaoParaCorpo(sub: PushSubscription, agente: string) {
  const chaves = sub.toJSON().keys ?? {}
  return {
    endpoint: sub.endpoint,
    chaveP256Dh: chaves['p256dh'] ?? '',
    chaveAuth: chaves['auth'] ?? '',
    agente,
  }
}

export function useNotificacoesPush() {
  const [estado, setEstado] = useState<EstadoDoPush>('carregando')
  const [chavePublica, setChavePublica] = useState<string | null>(null)

  // Suporte básico: service worker + PushManager
  const suportado =
    typeof window !== 'undefined' &&
    'serviceWorker' in navigator &&
    'PushManager' in window

  useEffect(() => {
    if (!suportado) {
      setEstado('nao-suportado')
      return
    }
    if (Notification.permission === 'denied') {
      setEstado('negado')
      return
    }

    // Consulta a chave pública — se 204 ou erro, push não está configurado no servidor.
    // GET não usa `enviar` (que é só para comandos); aqui não precisamos do anti-CSRF.
    fetch('/api/acesso/push/chave-publica', { credentials: 'same-origin' })
      .then(async (resp) => {
        if (resp.status === 204 || !resp.ok) {
          setEstado('nao-configurado')
          return
        }
        const dado = ChavePublicaSchema.parse(await resp.json())
        setChavePublica(dado.chavePublica)

        // Verifica se já existe inscrição ativa neste dispositivo.
        const reg = await navigator.serviceWorker.ready
        const sub = await reg.pushManager.getSubscription()
        setEstado(sub ? 'ativo' : 'inativo')
      })
      .catch(() => setEstado('nao-configurado'))
  }, [suportado])

  const ativar = async () => {
    if (!chavePublica) return
    setEstado('carregando')
    try {
      if (Notification.permission === 'default') {
        const permissao = await Notification.requestPermission()
        if (permissao !== 'granted') {
          setEstado('negado')
          return
        }
      }

      const reg = await navigator.serviceWorker.ready
      const sub = await reg.pushManager.subscribe({
        userVisibleOnly: true,
        applicationServerKey: base64UrlParaUint8Array(chavePublica),
      })

      const agente = navigator.userAgent.slice(0, 200)
      await enviar('/api/acesso/push/inscricao', inscricaoParaCorpo(sub, agente))

      setEstado('ativo')
    } catch {
      setEstado('inativo')
    }
  }

  const desativar = async () => {
    setEstado('carregando')
    try {
      const reg = await navigator.serviceWorker.ready
      const sub = await reg.pushManager.getSubscription()
      if (sub) {
        await enviar(
          `/api/acesso/push/inscricao?endpoint=${encodeURIComponent(sub.endpoint)}`,
          undefined,
          undefined,
          {},
          'DELETE',
        )
        await sub.unsubscribe()
      }
      setEstado('inativo')
    } catch {
      setEstado('inativo')
    }
  }

  return { estado, ativar, desativar }
}
