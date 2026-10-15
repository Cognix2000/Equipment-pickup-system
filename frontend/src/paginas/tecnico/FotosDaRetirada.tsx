import { Camera, LoaderCircle, RotateCw, TriangleAlert, X } from 'lucide-react'
import { useId } from 'react'
import { Button } from '@/components/ui/button'
import type { FotoNaTela } from '@/features/ordens/useFotosDaRetirada'
import { cn } from '@/lib/utils'

/** Abre a câmera traseira no celular (no computador, escolhe um arquivo). */
export function BotaoDeFoto({
  rotulo,
  aoEscolher,
  destaque = false,
  desabilitado = false,
}: {
  rotulo: string
  aoEscolher: (arquivo: File) => void
  destaque?: boolean
  desabilitado?: boolean
}) {
  const id = useId()
  return (
    <div className="flex">
      <input
        id={id}
        type="file"
        accept="image/*"
        capture="environment"
        disabled={desabilitado}
        className="peer sr-only"
        onChange={(e) => {
          const arquivo = e.target.files?.[0]
          e.target.value = '' // a mesma foto pode ser escolhida de novo
          if (arquivo) aoEscolher(arquivo)
        }}
      />
      <label
        htmlFor={id}
        className={cn(
          'inline-flex min-h-11 cursor-pointer items-center gap-2 rounded-[10px] px-4 text-[15px] font-semibold peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-azul peer-disabled:cursor-not-allowed peer-disabled:bg-desabilitado peer-disabled:text-[#3b4652]',
          destaque ? 'bg-azul text-white' : 'border border-azul bg-superficie text-azul',
        )}
      >
        <Camera aria-hidden className="size-5" />
        {rotulo}
      </label>
    </div>
  )
}

export function MiniaturaDaFoto({
  foto,
  descricao,
  aoRemover,
  aoTentarDeNovo,
}: {
  foto: FotoNaTela
  descricao: string
  aoRemover: () => void
  aoTentarDeNovo: () => void
}) {
  return (
    <li className="flex flex-col gap-1.5">
      <div className="relative">
        <img src={foto.url} alt={descricao} className="aspect-[4/3] w-full rounded-lg bg-fundo object-cover" />
        <Button
          type="button"
          variante="neutro"
          tamanho="icone"
          aria-label={`Remover ${descricao.toLowerCase()}`}
          onClick={aoRemover}
          className="absolute top-1 right-1 size-11 rounded-full"
        >
          <X aria-hidden />
        </Button>
      </div>
      {foto.estado === 'enviando' && (
        <p className="flex items-center gap-1.5 text-[13px] text-texto-suave">
          <LoaderCircle aria-hidden className="size-4 animate-spin motion-reduce:animate-none" />
          Enviando
        </p>
      )}
      {foto.estado === 'enviada' && <p className="text-[13px] font-medium text-sucesso">Enviada</p>}
      {foto.estado === 'falhou' && (
        <div className="flex flex-col items-start gap-1">
          <p className="flex items-center gap-1.5 text-[13px] font-medium text-erro">
            <TriangleAlert aria-hidden className="size-4" />
            Não enviou
          </p>
          <Button type="button" variante="fantasma" tamanho="compacto" onClick={aoTentarDeNovo} className="px-2">
            <RotateCw aria-hidden className="size-4" />
            Tentar de novo
          </Button>
        </div>
      )}
    </li>
  )
}
