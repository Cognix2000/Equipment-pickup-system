import type { ReactNode } from 'react'

/** Casca das telas de entrada: marca no topo, um cartão só, legível ao sol e com uma mão. */
export function LayoutDeAcesso({ titulo, subtitulo, children }: { titulo: string; subtitulo?: string; children: ReactNode }) {
  return (
    <div className="flex min-h-dvh flex-col bg-fundo">
      {/* Cabeçalho com a mesma largura útil do cartão (440 - 2×16), para o título alinhar com a borda dele. */}
      <header className="bg-marinho px-4 pt-10 pb-20 text-white">
        <div className="mx-auto max-w-[408px]">
          <p className="font-titulo text-lg font-bold">Retirada · Netwise</p>
          <h1 className="mt-6 text-[28px] leading-tight font-bold">{titulo}</h1>
          {subtitulo && <p className="mt-2 text-[15px] text-texto-claro">{subtitulo}</p>}
        </div>
      </header>
      <main className="mx-auto -mt-12 w-full max-w-[440px] flex-1 px-4 pb-10">
        <div className="flex flex-col gap-5 rounded-2xl bg-superficie p-5 shadow-[0_8px_24px_rgba(4,62,105,0.12)] sm:p-6">{children}</div>
      </main>
    </div>
  )
}

/** Campo com rótulo e erro ligados ao input (acessível para leitor de tela). */
export function Campo({
  id,
  rotulo,
  erro,
  ajuda,
  children,
}: {
  id: string
  rotulo: string
  erro?: string
  ajuda?: string
  children: ReactNode
}) {
  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={id} className="text-sm font-medium">
        {rotulo}
      </label>
      {children}
      {ajuda && !erro && (
        <p id={`${id}-ajuda`} className="text-[13px] text-texto-suave">
          {ajuda}
        </p>
      )}
      {erro && (
        <p id={`${id}-erro`} className="text-[13px] font-medium text-erro">
          {erro}
        </p>
      )}
    </div>
  )
}

export const classeDoInput =
  'min-h-12 w-full rounded-[10px] border border-borda-forte bg-superficie px-3 text-base text-texto placeholder:text-texto-suave/70 aria-[invalid=true]:border-erro'

export function AvisoDeErro({ mensagem }: { mensagem?: string }) {
  if (!mensagem) return null
  return (
    <p role="alert" className="rounded-[10px] bg-erro-fundo px-3.5 py-2.5 text-sm text-erro">
      {mensagem}
    </p>
  )
}
