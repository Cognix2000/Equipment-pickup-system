import { Check, WifiOff } from 'lucide-react'
import { AnimatePresence, motion, useReducedMotion } from 'motion/react'
import { lazy, Suspense, useMemo, useState } from 'react'
import { useLocation } from 'react-router'
import { Carregando, FalhaAoCarregar, Vazio } from '@/components/EstadoDaTela'
import { AvisoDeLocalizacao } from '@/features/mapa/AvisoDeLocalizacao'
import { useMinhaPosicao } from '@/features/mapa/minhaPosicao'
import { useOrdens } from '@/features/ordens/consultas'
import { OrdemCard } from '@/features/ordens/OrdemCard'
import { distanciaCurta, ordenarOrdens, type CriterioDeOrdenacao } from '@/features/ordens/ordenacao'
import { ResumoDoTecnico } from '@/features/ordens/ResumoDoTecnico'
import { ARecebe } from '@/features/repasses/ARecebe'
import { caixaDeTitulo } from '@/features/ordens/tipos'
import { useConectado } from '@/lib/useConectado'
import { cn } from '@/lib/utils'

// O mapa (MapLibre) é a parte mais pesada: carrega depois da lista.
const MapaDeOrdens = lazy(() => import('@/features/mapa/MapaDeOrdens'))

export function MinhasOrdens() {
  // Sem login ainda (RF01): mostra as atribuídas. Com login, filtra pelo técnico.
  const { data: ordens, isPending, isError, error, refetch } = useOrdens({ status: 'atribuida' })
  const [bairro, setBairro] = useState<string | null>(null)
  const [criterio, setCriterio] = useState<CriterioDeOrdenacao>('proximidade')
  const { posicao: minhaPosicao } = useMinhaPosicao()
  const conectado = useConectado()
  const reduzirMovimento = useReducedMotion()
  // Aviso de quem acabou de registrar uma retirada ou visita.
  const aviso = (useLocation().state as { aviso?: string } | null)?.aviso

  const bairros = useMemo(() => {
    const contagem = new Map<string, number>()
    for (const o of ordens ?? []) contagem.set(o.endereco.bairro, (contagem.get(o.endereco.bairro) ?? 0) + 1)
    return [...contagem].sort(([a], [b]) => a.localeCompare(b, 'pt-BR'))
  }, [ordens])

  // Filtra por bairro (se marcado) e ordena pelo critério escolhido pelo técnico.
  const visiveis = useMemo(() => {
    const filtradas = (ordens ?? []).filter((o) => !bairro || o.endereco.bairro === bairro)
    return ordenarOrdens(filtradas, criterio, minhaPosicao)
  }, [ordens, bairro, criterio, minhaPosicao])

  return (
    <>
      <header className="flex flex-col gap-1 bg-marinho px-5 pt-5 pb-4 text-white">
        <div className="flex items-center justify-between gap-3">
          <h1 className="text-[22px] font-bold">Minhas ordens</h1>
          <span className={cn('flex items-center gap-1.5 text-[13px]', conectado ? 'text-ciano' : 'text-alerta-fundo')}>
            {conectado ? <Check aria-hidden className="size-4" /> : <WifiOff aria-hidden className="size-4" />}
            {conectado ? 'Conectado' : 'Sem sinal'}
          </span>
        </div>
        <p className="text-sm text-texto-claro">
          {isPending ? 'Carregando suas ordens' : `${ordens?.length ?? 0} ${ordens?.length === 1 ? 'ordem atribuída' : 'ordens atribuídas'} a você`}
        </p>
        <ResumoDoTecnico />
        <ARecebe />
      </header>

      {bairros.length > 1 && (
        <div role="group" aria-label="Filtrar por bairro" className="flex gap-2 overflow-x-auto border-b border-borda bg-superficie px-4 py-3">
          <Filtro ativo={bairro === null} onClick={() => setBairro(null)}>
            Todas · {ordens?.length}
          </Filtro>
          {bairros.map(([nome, total]) => (
            <Filtro key={nome} ativo={bairro === nome} onClick={() => setBairro(nome)}>
              {caixaDeTitulo(nome)} · {total}
            </Filtro>
          ))}
        </div>
      )}

      {(ordens?.length ?? 0) > 1 && (
        <div role="group" aria-label="Ordenar por" className="flex gap-2 overflow-x-auto border-b border-borda bg-superficie px-4 py-3">
          <Filtro ativo={criterio === 'proximidade'} onClick={() => setCriterio('proximidade')}>
            {minhaPosicao ? 'Mais próxima' : 'Mais próxima (sem GPS)'}
          </Filtro>
          <Filtro ativo={criterio === 'urgencia'} onClick={() => setCriterio('urgencia')}>
            Mais urgente
          </Filtro>
          <Filtro ativo={criterio === 'bairro'} onClick={() => setCriterio('bairro')}>
            Por bairro
          </Filtro>
        </div>
      )}

      {visiveis.length > 0 && (
        <Suspense fallback={<div className="h-[250px] bg-mapa" />}>
          <MapaDeOrdens ordens={visiveis} className="h-[250px] w-full shrink-0" />
        </Suspense>
      )}

      <main className="flex flex-1 flex-col gap-3 p-4">
        <AvisoDeLocalizacao />
        {aviso && (
          <p role="status" className="flex items-start gap-2 rounded-[10px] bg-sucesso-fundo px-3.5 py-2.5 text-sm font-medium text-sucesso">
            <Check aria-hidden className="mt-0.5 size-4 shrink-0" />
            {aviso}
          </p>
        )}
        {isPending && <Carregando rotulo="Carregando ordens" />}
        {isError && <FalhaAoCarregar erro={error} tentarDeNovo={() => void refetch()} />}
        {!isPending && !isError && visiveis.length === 0 && (
          <Vazio titulo="Nenhuma ordem com você agora">Quando o gestor designar uma ordem, ela aparece aqui e no mapa.</Vazio>
        )}

        <ol className="flex flex-col gap-3">
          <AnimatePresence initial={false}>
            {visiveis.map((o, i) => (
              <motion.li
                key={o.id}
                layout={!reduzirMovimento}
                initial={reduzirMovimento ? false : { opacity: 0 }}
                animate={{ opacity: 1 }}
                exit={reduzirMovimento ? undefined : { opacity: 0 }}
                transition={{ duration: 0.18 }}
              >
                <OrdemCard ordem={o} posicao={i + 1} sugerida={i === 0} distancia={distanciaCurta(o, minhaPosicao)} />
              </motion.li>
            ))}
          </AnimatePresence>
        </ol>
      </main>
    </>
  )
}

function Filtro({ ativo, onClick, children }: { ativo: boolean; onClick: () => void; children: React.ReactNode }) {
  return (
    <button
      type="button"
      aria-pressed={ativo}
      onClick={onClick}
      className={cn(
        'min-h-11 shrink-0 rounded-full px-4 text-sm font-medium transition-colors motion-reduce:transition-none',
        ativo ? 'bg-azul text-white' : 'border border-[#c5cdd6] bg-superficie text-texto hover:bg-fundo',
      )}
    >
      {children}
    </button>
  )
}
