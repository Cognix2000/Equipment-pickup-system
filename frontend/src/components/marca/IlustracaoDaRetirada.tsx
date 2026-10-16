/**
 * Ilustração das telas de acesso: a retirada vista de cima. A rota de fibra (ciano) sai da casa do cliente,
 * onde a ONU foi recolhida, e chega à loja da Netwise, marcada com o pin da marca (o mesmo do favicon).
 *
 * São duas composições com as mesmas peças, para ficar inteira em qualquer tela:
 * - CenaDaRetirada: compacta, na coluna do formulário, em celular e tablet (até 1023 px);
 * - MapaDaRetirada: larga, no fundo do cabeçalho; a casa e a loja ficam dos lados do formulário a partir
 *   de 1024 px, e o mapa da cidade aparece como textura em todas as larguras.
 *
 * Decorativas (aria-hidden). As cores saem dos tokens do index.css. A rota se desenha uma vez ao abrir
 * (classe ilustracao-rota); com movimento reduzido, já aparece pronta.
 */

const contorno = { strokeLinecap: 'round', strokeLinejoin: 'round', strokeWidth: 2.5 } as const

/** Casa do cliente com a ONU na porta. Origem no canto de cima; a base fica em y=60 e a porta em x=44. */
function Casa({ x, y, escala = 1 }: { x: number; y: number; escala?: number }) {
  return (
    <g transform={`translate(${x} ${y}) scale(${escala})`} {...contorno}>
      <path d="M0 60 V26 L30 2 L60 26 V60 Z" className="fill-marinho stroke-white" />
      <rect x="10" y="32" width="14" height="12" rx="3" className="fill-ciano" strokeWidth={0} />
      <path d="M38 60 V40 H50 V60" className="fill-none stroke-white" />
      <rect x="66" y="48" width="32" height="12" rx="4" className="fill-white" strokeWidth={0} />
      <circle cx="74" cy="54" r="2" className="fill-azul" strokeWidth={0} />
      <circle cx="82" cy="54" r="2" className="fill-azul" strokeWidth={0} />
      <circle cx="90" cy="54" r="2" className="fill-ciano" strokeWidth={0} />
    </g>
  )
}

/** Loja da Netwise com o pin da marca em cima. A base fica em y=40 e a porta em x=46. */
function Loja({ x, y, escala = 1 }: { x: number; y: number; escala?: number }) {
  return (
    <g transform={`translate(${x} ${y}) scale(${escala})`} {...contorno}>
      <rect x="0" y="0" width="92" height="40" rx="6" className="fill-marinho stroke-white" />
      <rect x="0" y="0" width="92" height="11" rx="5" className="fill-azul stroke-white" />
      <path d="M36 40 V22 H56 V40" className="fill-none stroke-white" />
      <rect x="10" y="18" width="14" height="10" rx="2" className="fill-white/25" strokeWidth={0} />
      <rect x="68" y="18" width="14" height="10" rx="2" className="fill-white/25" strokeWidth={0} />
      <g transform="translate(20 -63.8) scale(1.3)">
        <path d="M20 46 10 28a12 12 0 1 1 20 0z" className="fill-white" strokeWidth={0} />
        <circle cx="20" cy="19" r="5" className="fill-ciano" strokeWidth={0} />
      </g>
    </g>
  )
}

function Rota({ d }: { d: string }) {
  return (
    <path
      d={d}
      pathLength={1}
      className="ilustracao-rota fill-none stroke-ciano"
      strokeWidth={3}
      strokeLinecap="round"
      strokeLinejoin="round"
    />
  )
}

/** Versão compacta: cabe na coluna do formulário e escala com ela (de 288 a 408 px de largura). */
export function CenaDaRetirada({ className }: { className?: string }) {
  return (
    <svg aria-hidden focusable="false" viewBox="0 0 408 120" className={className}>
      <circle cx="128" cy="70" r="3.5" className="fill-ciano/40" />
      <circle cx="190" cy="72" r="3.5" className="fill-ciano/40" />
      <circle cx="266" cy="64" r="3.5" className="fill-ciano/40" />
      <Rota d="M58 104 V112 H150 V88 H236 V112 H346 V104" />
      <Casa x={14} y={44} />
      <Loja x={300} y={64} />
    </svg>
  )
}

// Quadras da cidade simulada: colunas e linhas irregulares, como num mapa de bairro.
const colunas = [0, 96, 214, 300, 404, 520, 628, 736, 852, 960, 1068, 1172, 1280, 1376, 1440]
const linhas = [12, 92, 172]
// O mapa fica mais apagado no alto, para não competir com o título.
const brilhoDaLinha = [0.02, 0.035, 0.05]
const quadras = linhas.flatMap((y, l) =>
  colunas.slice(1).map((fim, c) => {
    const x = colunas[c] ?? 0
    // Algumas quadras somem para abrir praças e deixar o mapa menos regular.
    const vazia = (c * 7 + l * 3) % 9 === 0
    return { x: x + 10, y, largura: fim - x - 20, vazia, brilho: brilhoDaLinha[l] }
  }),
)

// Outras ordens espalhadas pelo mapa, fora da coluna do texto (só textura, sem dado nenhum).
const outrasOrdens = [
  [205, 82], [456, 162], [946, 162], [1164, 82], [1272, 162],
]

/** Versão larga, no fundo do cabeçalho: 1440 px, centralizada e presa ao rodapé dele. */
export function MapaDaRetirada({ className }: { className?: string }) {
  return (
    <svg aria-hidden focusable="false" viewBox="0 0 1440 280" width={1440} height={280} className={className}>
      {quadras.map(
        (q, i) =>
          !q.vazia && <rect key={i} x={q.x} y={q.y} width={q.largura} height={60} rx={10} fill="#fff" fillOpacity={q.brilho} />,
      )}
      {/* Os detalhes só entram onde sobra espaço dos lados do formulário (1024 px ou mais). */}
      <g className="hidden lg:inline">
        {outrasOrdens.map(([x, y]) => (
          <circle key={`${x}-${y}`} cx={x} cy={y} r={4} className="fill-ciano/40" />
        ))}
        {/* Da porta do cliente até a porta da loja; o trecho do meio passa por baixo do cartão. */}
        <Rota d="M345 226 V246 H520 V252 H940 V246 H1056 V262 H1125 V244" />
        <Casa x={292} y={154} escala={1.2} />
        <Loja x={1070} y={196} escala={1.2} />
      </g>
    </svg>
  )
}
