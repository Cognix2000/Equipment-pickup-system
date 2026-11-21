import { LogOut } from 'lucide-react'
import { useEffect, useRef, useState, type ReactNode } from 'react'
import { Button } from '@/components/ui/button'
import { useSair } from './useSair'

/**
 * Botão de sair com confirmação (nov/2026): o toque acidental no menu do técnico ou no cabeçalho
 * do painel não desloga mais. Usa `<dialog>` nativo — foca sozinho, ESC fecha, acessível por padrão,
 * sem dependência nova. O `children` é o conteúdo do botão (ícone + texto); `className` estiliza.
 */
export function BotaoDeSair({ className, children }: { className?: string; children: ReactNode }) {
  const sair = useSair()
  const [saindo, setSaindo] = useState(false)
  const dialog = useRef<HTMLDialogElement>(null)

  useEffect(() => {
    const d = dialog.current
    if (!d) return
    if (saindo) return // enquanto a saída processa, o diálogo fica aberto com os botões desabilitados
  }, [saindo])

  const abrir = () => dialog.current?.showModal()
  const fechar = () => dialog.current?.close()
  const confirmar = async () => {
    setSaindo(true)
    try {
      await sair()
    } finally {
      setSaindo(false)
      // `sair()` redireciona pra /entrar; se por algum motivo não redirecionar, o modal fecha.
      fechar()
    }
  }

  return (
    <>
      <button type="button" className={className} onClick={abrir}>
        {children}
      </button>
      <dialog
        ref={dialog}
        className="rounded-xl bg-superficie p-5 text-marinho shadow-xl backdrop:bg-black/40 open:flex open:flex-col open:gap-4 open:w-[min(360px,calc(100vw-2rem))] open:m-auto"
      >
        <div>
          <h2 className="text-base font-semibold">Sair da conta?</h2>
          <p className="mt-1 text-[14px] text-texto-suave">Você vai precisar entrar de novo com o e-mail e a senha.</p>
        </div>
        <div className="flex flex-wrap justify-end gap-2">
          <Button type="button" variante="fantasma" onClick={fechar} disabled={saindo}>
            Cancelar
          </Button>
          <Button type="button" variante="perigo" onClick={() => void confirmar()} disabled={saindo}>
            {saindo ? 'Saindo' : 'Sair'}
          </Button>
        </div>
      </dialog>
    </>
  )
}

/** Variante de ícone + texto, para o menu inferior do técnico. */
export function BotaoDeSairNoMenu() {
  return (
    <BotaoDeSair className="flex flex-col items-center justify-center gap-0.5 text-xs text-texto-suave [&_svg]:size-[22px]">
      <LogOut aria-hidden />
      Sair
    </BotaoDeSair>
  )
}
