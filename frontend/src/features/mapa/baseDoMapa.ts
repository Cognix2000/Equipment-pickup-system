import 'maplibre-gl/dist/maplibre-gl.css'
import { Map as MapaMapLibre, Marker, setWorkerUrl, type StyleSpecification } from 'maplibre-gl'
import urlDoWorker from 'maplibre-gl/dist/maplibre-gl-worker.mjs?url'
import type { Gps } from '@/features/ordens/comandos'
import type { EstiloDoMapa } from './localizacao'

// O MapLibre 6 procura o worker ao lado do próprio arquivo; empacotado, o Vite muda o
// endereço. Apontar o worker aqui faz o mapa funcionar no build e no PWA offline.
setWorkerUrl(urlDoWorker)

const cores = { fundo: '#e7eef3', bairro: '#dbe6ee', borda: '#c5d3de', rua: '#ffffff' }

/** Homologação: a cidade fictícia, sem servidor de mapas (o fundo é um GeoJSON do próprio app). */
const cidadeSimulada: StyleSpecification = {
  version: 8,
  sources: { cidade: { type: 'geojson', data: '/dados-ficticios/cidade_simulada.geojson' } },
  layers: [
    { id: 'fundo', type: 'background', paint: { 'background-color': cores.fundo } },
    { id: 'bairros', type: 'fill', source: 'cidade', filter: ['==', ['get', 'tipo'], 'bairro'], paint: { 'fill-color': cores.bairro, 'fill-outline-color': cores.borda } },
    { id: 'ruas', type: 'line', source: 'cidade', filter: ['==', ['get', 'tipo'], 'rua'], paint: { 'line-color': cores.rua, 'line-width': ['interpolate', ['linear'], ['zoom'], 13, 3, 17, 12] } },
  ],
}

/**
 * Produção: ruas de verdade do OpenFreeMap (dados do OpenStreetMap). Grátis, sem chave e com uso comercial
 * permitido; o MapLibre mostra a atribuição exigida no canto do mapa.
 */
const ruasReais = 'https://tiles.openfreemap.org/styles/liberty'

export const CentroDaCidadeSimulada: [number, number] = [-38.99, -21.0]

export function criarMapa(container: HTMLElement, estilo: EstiloDoMapa) {
  return new MapaMapLibre({
    container,
    style: estilo === 'real' ? ruasReais : cidadeSimulada,
    center: CentroDaCidadeSimulada,
    zoom: 14,
    // A atribuição do OpenStreetMap é obrigatória no mapa real; a cidade simulada não usa dado de ninguém.
    attributionControl: estilo === 'real' ? { compact: true } : false,
    cooperativeGestures: true, // rolar a página não dá zoom sem querer
    maxPitch: 75, // deixa o 3D funcionar; o default do MapLibre é 60°
  })
}

/**
 * Prédios 3D sobre o mapa real (OpenFreeMap / OpenMapTiles). Puxa a extrusão da altura "render_height"
 * quando o OpenMapTiles traz, senão estima pelo número de andares (3 m por andar) ou usa 6 m como mínimo.
 *
 * Só vale no estilo real: a cidade simulada não tem prédios. Esconder o layer plano "building" do estilo
 * evita o duplicado visual (uma camada plana por baixo e a extrusão por cima). Chamar duas vezes é inócuo:
 * o addLayer só entra quando o layer ainda não existe.
 */
export function habilitarEdificios3D(mapa: MapaMapLibre, estilo: EstiloDoMapa) {
  if (estilo !== 'real') return
  const aplicar = () => {
    if (mapa.getLayer('building')) mapa.setLayoutProperty('building', 'visibility', 'none')
    if (mapa.getLayer('edificios-3d')) return
    if (!mapa.getSource('openmaptiles')) return // o estilo mudou; nada a fazer
    mapa.addLayer({
      id: 'edificios-3d',
      source: 'openmaptiles',
      'source-layer': 'building',
      type: 'fill-extrusion',
      minzoom: 14,
      paint: {
        'fill-extrusion-color': ['interpolate', ['linear'], ['coalesce', ['get', 'render_height'], 6], 0, '#e3e8ed', 10, '#d5dde3', 50, '#a9b6c1'],
        // Prefere a altura real; cai para 3 m × andares; se nada vier, assume 6 m (uma casa térrea com telhado).
        'fill-extrusion-height': ['coalesce', ['get', 'render_height'], ['*', ['coalesce', ['get', 'building:levels'], 2], 3]],
        'fill-extrusion-base': ['coalesce', ['get', 'render_min_height'], 0],
        'fill-extrusion-opacity': 0.9,
      },
    })
    // Luz suave vindo do nordeste para desenhar sombras discretas nos prédios.
    mapa.setLight({ anchor: 'map', position: [1.5, 60, 60], intensity: 0.25 })
  }
  if (mapa.isStyleLoaded()) aplicar()
  else mapa.once('load', aplicar)
}

/** Desliga o 3D (volta ao mapa plano): esconde a extrusão e mostra de novo o prédio 2D do estilo. */
export function desabilitarEdificios3D(mapa: MapaMapLibre) {
  if (mapa.getLayer('edificios-3d')) mapa.setLayoutProperty('edificios-3d', 'visibility', 'none')
  if (mapa.getLayer('building')) mapa.setLayoutProperty('building', 'visibility', 'visible')
}

/** Religa o 3D sem recriar o layer, quando o técnico volta a inclinar o mapa. */
export function reabilitarEdificios3D(mapa: MapaMapLibre) {
  if (mapa.getLayer('edificios-3d')) mapa.setLayoutProperty('edificios-3d', 'visibility', 'visible')
  if (mapa.getLayer('building')) mapa.setLayoutProperty('building', 'visibility', 'none')
}

/** Enquadra os pontos; com um só, centraliza nele. */
export function enquadrar(mapa: MapaMapLibre, pontos: [number, number][]) {
  if (pontos.length === 1) mapa.jumpTo({ center: pontos[0], zoom: 15 })
  if (pontos.length < 2) return
  const lngs = pontos.map((p) => p[0])
  const lats = pontos.map((p) => p[1])
  mapa.fitBounds([[Math.min(...lngs), Math.min(...lats)], [Math.max(...lngs), Math.max(...lats)]], { padding: 48, maxZoom: 16, animate: false })
}

/** O ponto azul de "você está aqui", como nos apps de mapa. */
export function pontoDaMinhaPosicao(posicao: Gps) {
  const ponto = document.createElement('div')
  ponto.setAttribute('role', 'img')
  ponto.setAttribute('aria-label', 'Você está aqui')
  ponto.className = 'size-[18px] rounded-full border-[3px] border-white bg-azul shadow-[0_0_0_6px_rgba(0,89,189,0.25)]'
  return new Marker({ element: ponto }).setLngLat([posicao.longitude, posicao.latitude])
}
