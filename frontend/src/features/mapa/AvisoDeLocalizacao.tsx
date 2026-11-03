import { LocateFixed, LocateOff } from 'lucide-react'
import { useMinhaPosicao } from './minhaPosicao'

/** Transparência com o técnico: diz quando a posição dele aparece para o gestor, e o que fazer se o celular bloqueou. */
export function AvisoDeLocalizacao() {
  const { situacao } = useMinhaPosicao()
  if (situacao === 'desligada') return null

  if (situacao === 'negada' || situacao === 'indisponivel')
    return (
      <p role="status" className="flex items-start gap-2 rounded-[10px] bg-alerta-fundo px-3.5 py-2.5 text-sm font-medium text-alerta">
        <LocateOff aria-hidden className="mt-0.5 size-4 shrink-0" />
        {situacao === 'negada'
          ? 'A localização está bloqueada: o gestor não vê onde você está. Libere a localização deste site nas configurações do navegador.'
          : 'Este aparelho não informa a localização: o gestor não vê onde você está.'}
      </p>
    )

  return (
    <p className="flex items-start gap-2 text-[13px] text-texto-suave">
      <LocateFixed aria-hidden className="mt-0.5 size-4 shrink-0 text-azul" />
      Sua localização aparece para o gestor enquanto o app está aberto.
    </p>
  )
}
