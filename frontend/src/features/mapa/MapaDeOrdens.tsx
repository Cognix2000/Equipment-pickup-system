import 'maplibre-gl/dist/maplibre-gl.css'
import { Map as MapaMapLibre, Marker, setWorkerUrl, type StyleSpecification } from 'maplibre-gl'
import urlDoWorker from 'maplibre-gl/dist/maplibre-gl-worker.mjs?url'
import { useEffect, useRef } from 'react'
import { useNavigate } from 'react-router'
import type { Ordem } from '@/features/ordens/tipos'

/*
  Mapa da cidade simulada, sem servidor de mapas: o fundo é o GeoJSON fictício.
  Quando a API de mapa chegar (incremento 4), só o `estilo` muda, atrás do adaptador.
*/
// O MapLibre 6 procura o worker ao lado do próprio arquivo; empacotado, o Vite muda o
// endereço. Apontar o worker aqui faz o mapa funcionar no build e no PWA offline.
setWorkerUrl(urlDoWorker)

const cores = { fundo: '#e7eef3', bairro: '#dbe6ee', borda: '#c5d3de', rua: '#ffffff' }

const estilo: StyleSpecification = {
  version: 8,
  sources: { cidade: { type: 'geojson', data: '/dados-ficticios/cidade_simulada.geojson' } },
  layers: [
    { id: 'fundo', type: 'background', paint: { 'background-color': cores.fundo } },
    { id: 'bairros', type: 'fill', source: 'cidade', filter: ['==', ['get', 'tipo'], 'bairro'], paint: { 'fill-color': cores.bairro, 'fill-outline-color': cores.borda } },
    { id: 'ruas', type: 'line', source: 'cidade', filter: ['==', ['get', 'tipo'], 'rua'], paint: { 'line-color': cores.rua, 'line-width': ['interpolate', ['linear'], ['zoom'], 13, 3, 17, 12] } },
  ],
}

type Props = { ordens: Ordem[]; className?: string }

export default function MapaDeOrdens({ ordens, className }: Props) {
  const elemento = useRef<HTMLDivElement>(null)
  const navegar = useNavigate()

  useEffect(() => {
    if (!elemento.current) return
    const comPonto = ordens.filter((o) => o.localizacao)
    const mapa = new MapaMapLibre({
      container: elemento.current,
      style: estilo,
      center: [-38.99, -21.0],
      zoom: 14,
      attributionControl: false,
      cooperativeGestures: true, // rolar a página não dá zoom sem querer
    })

    const marcadores = comPonto.map((o, i) => {
      const pino = document.createElement('button')
      pino.type = 'button'
      pino.textContent = String(i + 1)
      pino.setAttribute('aria-label', `Abrir ordem ${i + 1}: ${o.endereco.logradouro}, ${o.endereco.numero ?? 's/ nº'}`)
      pino.className =
        'grid size-9 place-items-center rounded-full border-[3px] border-white text-sm font-semibold text-white shadow-md ' +
        (i === 0 ? 'bg-azul' : 'bg-marinho')
      pino.addEventListener('click', () => navegar(`/tecnico/ordens/${o.id}`))
      return new Marker({ element: pino }).setLngLat([o.localizacao!.longitude, o.localizacao!.latitude]).addTo(mapa)
    })

    if (comPonto.length > 1) {
      const lngs = comPonto.map((o) => o.localizacao!.longitude)
      const lats = comPonto.map((o) => o.localizacao!.latitude)
      mapa.fitBounds([[Math.min(...lngs), Math.min(...lats)], [Math.max(...lngs), Math.max(...lats)]], { padding: 48, maxZoom: 16, animate: false })
    }

    return () => {
      marcadores.forEach((m) => m.remove())
      mapa.remove()
    }
  }, [ordens, navegar])

  return <div ref={elemento} className={className} role="region" aria-label="Mapa das ordens" />
}
