import { CloudUpload, LogOut, Map as IconeMapa } from 'lucide-react'
import { NavLink, Outlet } from 'react-router'
import { useSair } from '@/features/acesso/useSair'
import { cn } from '@/lib/utils'

/**
 * Casca do app de campo: coluna de celular. Em tablet ou computador fica centralizada
 * em até 560 px, para continuar usável com uma mão (RNF02).
 */
export function LayoutTecnico() {
  const sair = useSair()
  return (
    <div className="min-h-dvh bg-moldura">
      <div className="mx-auto flex min-h-dvh w-full max-w-[560px] flex-col bg-fundo">
        <div className="flex flex-1 flex-col">
          <Outlet />
        </div>
        <nav aria-label="Navegação principal" className="sticky bottom-0 grid h-16 grid-cols-3 border-t border-borda bg-superficie pb-[env(safe-area-inset-bottom)]">
          <ItemDeNavegacao para="/tecnico/ordens" icone={<IconeMapa aria-hidden />} texto="Ordens" />
          <ItemDeNavegacao para="/tecnico/envios" icone={<CloudUpload aria-hidden />} texto="Envios" />
          <button
            type="button"
            onClick={() => void sair()}
            className="flex flex-col items-center justify-center gap-0.5 text-xs text-texto-suave [&_svg]:size-[22px]"
          >
            <LogOut aria-hidden />
            Sair
          </button>
        </nav>
      </div>
    </div>
  )
}

function ItemDeNavegacao({ para, icone, texto }: { para: string; icone: React.ReactNode; texto: string }) {
  return (
    <NavLink
      to={para}
      className={({ isActive }) =>
        cn('flex flex-col items-center justify-center gap-0.5 text-xs no-underline [&_svg]:size-[22px]', isActive ? 'font-semibold text-azul' : 'text-texto-suave')
      }
    >
      {icone}
      {texto}
    </NavLink>
  )
}
