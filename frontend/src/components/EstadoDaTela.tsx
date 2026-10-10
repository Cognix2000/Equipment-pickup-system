import { TriangleAlert } from 'lucide-react'
import type { ReactNode } from 'react'
import { Button } from '@/components/ui/button'
import { ErroDaApi } from '@/lib/api'

/** Carregando: blocos no formato do conteúdo, sem girar nada na tela. */
export function Carregando({ linhas = 3, rotulo }: { linhas?: number; rotulo: string }) {
  return (
    <div role="status" aria-label={rotulo} className="flex flex-col gap-3">
      {Array.from({ length: linhas }, (_, i) => (
        <div key={i} className="h-20 animate-pulse rounded-xl bg-superficie" />
      ))}
    </div>
  )
}

/** Erro com o que aconteceu e o que fazer, na voz da interface. */
export function FalhaAoCarregar({ erro, tentarDeNovo }: { erro: unknown; tentarDeNovo: () => void }) {
  const mensagem =
    erro instanceof ErroDaApi
      ? erro.message
      : 'Sem resposta do servidor. Confira a conexão e tente de novo.'
  return (
    <div role="alert" className="flex flex-col items-start gap-3 rounded-xl bg-alerta-fundo p-4 text-alerta">
      <p className="flex items-center gap-2 font-medium">
        <TriangleAlert aria-hidden className="size-5 shrink-0" />
        {mensagem}
      </p>
      <Button variante="neutro" tamanho="compacto" onClick={tentarDeNovo}>
        Tentar de novo
      </Button>
    </div>
  )
}

export function Vazio({ titulo, children }: { titulo: string; children?: ReactNode }) {
  return (
    <div className="flex flex-col items-center gap-1 rounded-xl bg-superficie px-6 py-10 text-center">
      <p className="font-titulo text-lg font-bold text-marinho">{titulo}</p>
      {children && <p className="max-w-[40ch] text-sm text-texto-suave">{children}</p>}
    </div>
  )
}
