import { useSyncExternalStore } from 'react'

function assinar(avisar: () => void) {
  window.addEventListener('online', avisar)
  window.addEventListener('offline', avisar)
  return () => {
    window.removeEventListener('online', avisar)
    window.removeEventListener('offline', avisar)
  }
}

/** Se o aparelho tem rede agora. Iniciar a retirada exige estar online (RN07). */
export function useConectado() {
  return useSyncExternalStore(assinar, () => navigator.onLine, () => true)
}
