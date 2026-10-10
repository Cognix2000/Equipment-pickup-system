import { Link } from 'react-router'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import { descreverPrazo } from './prazo'
import { caixaDeTitulo, enderecoCurto, resumoDosItens, type Ordem } from './tipos'

type Props = {
  ordem: Ordem
  posicao: number
  /** A primeira da lista ganha destaque e o botão de abrir. */
  sugerida?: boolean
  agora?: Date
}

export function OrdemCard({ ordem, posicao, sugerida = false, agora }: Props) {
  const prazo = descreverPrazo(ordem.carenciaAte, agora)
  const detalhe = `/tecnico/ordens/${ordem.id}`

  if (!sugerida) {
    return (
      <Link
        to={detalhe}
        className="flex items-center justify-between gap-3 rounded-xl bg-superficie px-4 py-3.5 text-left no-underline hover:bg-info-fundo/40"
      >
        <span className="min-w-0">
          <span className="block text-base font-semibold text-marinho">
            {posicao} · {enderecoCurto(ordem)}
          </span>
          <span className="block text-[13px] text-texto-suave">
            {caixaDeTitulo(ordem.endereco.bairro)} · {resumoDosItens(ordem)}
          </span>
        </span>
        <span className={cn('shrink-0 text-xs', prazo.vencido ? 'font-semibold text-alerta' : 'text-texto-suave')}>{prazo.texto}</span>
      </Link>
    )
  }

  return (
    <article className="flex flex-col gap-2.5 rounded-xl border-2 border-azul bg-superficie p-4" aria-label={`Ordem sugerida: ${enderecoCurto(ordem)}`}>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span className="text-[13px] font-semibold text-azul">{posicao} · Mais urgente</span>
        <Badge tom={prazo.vencido ? 'alerta' : 'neutro'}>{prazo.texto}</Badge>
      </div>
      <div>
        <h2 className="text-lg font-bold text-marinho">{enderecoCurto(ordem)}</h2>
        <p className="mt-0.5 text-sm text-texto-suave">
          {caixaDeTitulo(ordem.endereco.bairro)} · OS {ordem.numeroOs ?? 's/ nº'} · {ordem.tipoOs}
        </p>
      </div>
      <p className="text-sm">Recolher: {resumoDosItens(ordem)}</p>
      <Button asChild>
        <Link to={detalhe}>Ver ordem</Link>
      </Button>
    </article>
  )
}
