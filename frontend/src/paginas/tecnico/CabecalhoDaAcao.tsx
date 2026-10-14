import { ChevronLeft } from 'lucide-react'
import { Link } from 'react-router'

/** Cabeçalho das telas de ação (retirada, visita): volta para o detalhe da ordem. */
export function CabecalhoDaAcao({ titulo, subtitulo, voltarPara }: { titulo: string; subtitulo?: string; voltarPara: string }) {
  return (
    <header className="flex items-center gap-2 bg-marinho px-3 pt-3 pb-4 text-white">
      <Link to={voltarPara} aria-label="Voltar para a ordem" className="grid size-12 place-items-center text-white">
        <ChevronLeft aria-hidden className="size-6" />
      </Link>
      <div className="min-w-0 flex-1">
        <h1 className="text-xl font-bold">{titulo}</h1>
        {subtitulo && <p className="truncate text-[13px] text-texto-claro">{subtitulo}</p>}
      </div>
    </header>
  )
}
