import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { cn } from '@/lib/utils'
import { resumoDoTecnicoQuery, type ResumoDoTecnico as Resumo } from './consultas'

type Periodo = 'hoje' | 'semana' | 'mes'

const periodos: { valor: Periodo; texto: string }[] = [
  { valor: 'hoje', texto: 'Hoje' },
  { valor: 'semana', texto: 'Semana' },
  { valor: 'mes', texto: 'Mês' },
]

/**
 * Controle do técnico, no topo da tela dele: quantas recolheu, quantas o estoque já conferiu (as que contam
 * para o valor a receber) e as visitas sem sucesso. Se a consulta falhar, some: a lista de ordens é o principal.
 */
export function ResumoDoTecnico() {
  const { data, isPending, isError } = useQuery(resumoDoTecnicoQuery)
  const [periodo, setPeriodo] = useState<Periodo>('hoje')
  if (isError) return null

  return (
    <section aria-label="Seu resumo" className="flex flex-col gap-2.5 pt-1">
      <div role="group" aria-label="Período do resumo" className="flex gap-1 self-start rounded-full bg-white/10 p-1">
        {periodos.map((p) => (
          <button
            key={p.valor}
            type="button"
            aria-pressed={periodo === p.valor}
            onClick={() => setPeriodo(p.valor)}
            className={cn(
              'min-h-9 rounded-full px-3.5 text-[13px] font-semibold transition-colors motion-reduce:transition-none',
              periodo === p.valor ? 'bg-white text-marinho' : 'text-texto-claro hover:bg-white/10',
            )}
          >
            {p.texto}
          </button>
        ))}
      </div>
      {isPending ? (
        <div role="status" aria-label="Carregando o resumo" className="h-[68px] animate-pulse rounded-xl bg-white/10" />
      ) : (
        <Numeros resumo={data} periodo={periodo} />
      )}
    </section>
  )
}

function Numeros({ resumo, periodo }: { resumo: Resumo; periodo: Periodo }) {
  const c = resumo[periodo]
  return (
    <>
      <dl className="grid grid-cols-3 gap-2">
        <Numero rotulo="Retiradas" valor={c.retiradas} />
        <Numero rotulo="Conferidas" valor={c.conferidas} />
        <Numero rotulo="Sem sucesso" valor={c.visitasSemSucesso} />
      </dl>
      {resumo.aguardandoConferencia > 0 && (
        <p className="text-[13px] text-texto-claro">
          {resumo.aguardandoConferencia === 1
            ? '1 retirada esperando o estoque conferir'
            : `${resumo.aguardandoConferencia} retiradas esperando o estoque conferir`}
        </p>
      )}
    </>
  )
}

function Numero({ rotulo, valor }: { rotulo: string; valor: number }) {
  return (
    <div className="flex flex-col-reverse rounded-xl bg-white/10 px-3 py-2">
      <dt className="text-[13px] text-texto-claro">{rotulo}</dt>
      <dd className="font-titulo text-2xl leading-tight font-bold text-white tabular-nums">{valor}</dd>
    </div>
  )
}
