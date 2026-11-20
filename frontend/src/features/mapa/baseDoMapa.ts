import 'maplibre-gl/dist/maplibre-gl.css'
import { Map as MapaMapLibre, Marker, setWorkerUrl, type StyleSpecification } from 'maplibre-gl'
import urlDoWorker from 'maplibre-gl/dist/maplibre-gl-worker.mjs?url'
import type { Gps } from '@/features/ordens/comandos'
import type { EstiloDoMapa } from './localizacao'

// O MapLibre 6 procura o worker ao lado do próprio arquivo; empacotado, o Vite muda o
// endereço. Apontar o worker aqui faz o mapa funcionar no build e no PWA offline.
setWorkerUrl(urlDoWorker)

// Paleta "Imersivo 3D" (Pedro escolheu em nov/2026, comparativo em três opções):
// base bege quente, ruas em creme com avenida amarelada, água azul suave, prédios em bege
// com sombra lateral ambrada. Serve ao mesmo tempo o mapa simulado e o real — para o mapa
// real as mesmas cores vão sobrescrever o estilo Liberty do OpenFreeMap em aplicarEstiloImersivo().
const cores = {
  fundo: '#f1e6d2',
  bairro: '#e8dcc0',
  borda: '#d9c9a7',
  rua: '#f8ead0',
  ruaPrincipal: '#ffe8a8',
  parque: '#d9d5a8',
  agua: '#9cc6d9',
  predio: { topo: '#f4dcb4', meio: '#e2b981', base: '#c99c64' },
}

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
  const mapa = new MapaMapLibre({
    container,
    style: estilo === 'real' ? ruasReais : cidadeSimulada,
    center: CentroDaCidadeSimulada,
    zoom: 14,
    // A atribuição do OpenStreetMap é obrigatória no mapa real; a cidade simulada não usa dado de ninguém.
    attributionControl: estilo === 'real' ? { compact: true } : false,
    cooperativeGestures: true, // rolar a página não dá zoom sem querer
    maxPitch: 75, // deixa o 3D funcionar; o default do MapLibre é 60°
  })
  if (estilo === 'real') aplicarEstiloImersivo(mapa)
  return mapa
}

/**
 * Sobrescreve as cores do Liberty do OpenFreeMap para a paleta "Imersivo 3D" (Pedro, nov/2026).
 * Mantém a fonte de dados (OpenMapTiles) e a atribuição; muda só paint. É defensivo: ignora silenciosamente
 * se o estilo mudar de versão e um layer deixar de existir — o pior caso é o mapa aparecer na paleta original.
 */
export function aplicarEstiloImersivo(mapa: MapaMapLibre) {
  const aplicar = () => {
    const spec = mapa.getStyle()
    if (!spec?.layers) return
    for (const layer of spec.layers) {
      const id = layer.id.toLowerCase()
      try {
        if (layer.type === 'background') {
          mapa.setPaintProperty(layer.id, 'background-color', cores.fundo)
        } else if (layer.type === 'fill') {
          if (id.includes('water')) mapa.setPaintProperty(layer.id, 'fill-color', cores.agua)
          else if (id.includes('park') || id.includes('grass') || id.includes('wood') || id.includes('forest') || id.includes('scrub'))
            mapa.setPaintProperty(layer.id, 'fill-color', cores.parque)
          else if (id.includes('landuse') || id.includes('land_') || id.includes('residential') || id.includes('builtup'))
            mapa.setPaintProperty(layer.id, 'fill-color', cores.bairro)
        } else if (layer.type === 'line') {
          if (id.includes('water') || id.includes('river') || id.includes('stream'))
            mapa.setPaintProperty(layer.id, 'line-color', cores.agua)
          else if (id.includes('motorway') || id.includes('trunk') || id.includes('primary') || id.includes('major'))
            mapa.setPaintProperty(layer.id, 'line-color', cores.ruaPrincipal)
          else if (id.includes('road') || id.includes('street') || id.includes('path'))
            mapa.setPaintProperty(layer.id, 'line-color', cores.rua)
        }
      } catch {
        // Propriedade inexistente nessa versão do estilo: ignorar.
      }
    }
    try {
      // Céu azulado quando o técnico inclina o mapa — faz o 3D parecer imersivo mesmo.
      mapa.setSky({ 'sky-color': cores.agua, 'horizon-color': '#f3e3c2', 'horizon-fog-blend': 0.6 })
    } catch {
      // MapLibre antigo sem setSky: segue sem céu custom; o mapa continua bonito.
    }
  }
  if (mapa.isStyleLoaded()) aplicar()
  else mapa.once('load', aplicar)
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
        // Imersivo (nov/2026): prédios altos ficam no bege claro (topo iluminado), baixos no ambrado;
        // interpolação dá sensação de altura e combina com a paleta quente das ruas.
        'fill-extrusion-color': ['interpolate', ['linear'], ['coalesce', ['get', 'render_height'], 6],
          0, cores.predio.base, 10, cores.predio.meio, 50, cores.predio.topo],
        // Prefere a altura real; cai para 3 m × andares; se nada vier, assume 6 m (uma casa térrea com telhado).
        'fill-extrusion-height': ['coalesce', ['get', 'render_height'], ['*', ['coalesce', ['get', 'building:levels'], 2], 3]],
        'fill-extrusion-base': ['coalesce', ['get', 'render_min_height'], 0],
        'fill-extrusion-opacity': 0.92,
      },
    })
    // Luz lateral do nordeste, mais intensa que antes: desenha sombras ambradas nas laterais e dá o visual 3D
    // imersivo (a Opção 2 do comparativo). Se setLight falhar em alguma versão, o mapa segue sem sombra.
    try {
      mapa.setLight({ anchor: 'map', position: [1.5, 70, 50], intensity: 0.45, color: '#fff6e5' })
    } catch {
      // Sem luz customizada: a extrusão continua, só sem sombra ambrada.
    }
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
