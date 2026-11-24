/// <reference lib="webworker" />
import { cleanupOutdatedCaches, precacheAndRoute } from 'workbox-precaching'

declare let self: ServiceWorkerGlobalScope

// Workbox injeta o manifesto de pré-cache aqui em tempo de build.
precacheAndRoute(self.__WB_MANIFEST)
cleanupOutdatedCaches()

// ── Notificações push (VAPID) ────────────────────────────────────────────────

type PayloadPush = {
  titulo?: string
  corpo?: string
  url?: string
}

self.addEventListener('push', (event: PushEvent) => {
  let payload: PayloadPush = {}
  try {
    payload = (event.data?.json() as PayloadPush) ?? {}
  } catch {
    payload = { titulo: event.data?.text() ?? 'Nova notificação' }
  }

  const titulo = payload.titulo ?? 'Retirada Netwise'
  const opcoes: NotificationOptions = {
    body: payload.corpo,
    icon: '/icones/icone-192.png',
    badge: '/icones/favicon-64.png',
    data: { url: payload.url ?? '/' },
    // Agrupa notificações do mesmo app; substitui a anterior no Android.
    tag: 'retirada',
  }

  event.waitUntil(self.registration.showNotification(titulo, opcoes))
})

self.addEventListener('notificationclick', (event: NotificationEvent) => {
  event.notification.close()

  const url: string = (event.notification.data as { url?: string }).url ?? '/'
  event.waitUntil(
    self.clients
      .matchAll({ type: 'window', includeUncontrolled: true })
      .then((clientes) => {
        // Se já há uma aba aberta, foca nela e navega.
        for (const cliente of clientes) {
          if ('focus' in cliente) {
            void (cliente as WindowClient).navigate(url)
            return cliente.focus()
          }
        }
        // Nenhuma aba: abre uma nova.
        return self.clients.openWindow(url)
      }),
  )
})
