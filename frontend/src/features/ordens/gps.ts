import type { Gps } from './comandos'

export class GpsIndisponivel extends Error {}

/** Pede a posição na hora de confirmar (RF12). Sem permissão ou sem sinal, a ação não segue. */
export function obterGps(): Promise<Gps> {
  return new Promise((resolver, rejeitar) => {
    if (!('geolocation' in navigator)) return rejeitar(new GpsIndisponivel('Este aparelho não informa a localização.'))
    navigator.geolocation.getCurrentPosition(
      ({ coords }) => resolver({ latitude: coords.latitude, longitude: coords.longitude, precisaoM: Number.isFinite(coords.accuracy) ? Math.round(coords.accuracy) : null }),
      (erro) =>
        rejeitar(
          new GpsIndisponivel(
            erro.code === erro.PERMISSION_DENIED
              ? 'Permita o uso da localização para registrar: o GPS e a hora fazem parte do registro.'
              : 'Não consegui a localização agora. Vá para um lugar aberto e tente de novo.',
          ),
        ),
      { enableHighAccuracy: true, timeout: 15_000, maximumAge: 30_000 },
    )
  })
}
