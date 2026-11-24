import { CloudUpload, Map as IconeMapa, UserRound } from 'lucide-react'
import { NavLink, Outlet } from 'react-router'
import { useEu } from '@/features/acesso/acesso'
import { BotaoDeSairNoMenu } from '@/features/acesso/BotaoDeSair'
import { useCompartilharPosicao } from '@/features/mapa/minhaPosicao'
import { BannerDeInstalacao } from '@/features/pwa/BannerDeInstalacao'
import { cn } from '@/lib/utils'

/**
 * Casca do app de campo: coluna de celular. Em tablet ou computador fica centralizada
 * em até 560 px, para continuar usável com uma mão (RNF02).
 * Com o app aberto, o técnico compartilha a posição com o gestor (o gestor que abre esta tela, não).
 */
export function LayoutTecnico() {
  const { data: eu } = useEu()
  useCompartilharPosicao(eu?.papel === 'tecnico')
  return (
    <div className="min-h-dvh bg-moldura">
      <div className="mx-auto flex min-h-dvh w-full max-w-[560px] flex-col bg-fundo">
        <BannerDeInstalacao />
        <div className="flex flex-1 flex-col">
          <Outlet />
        </div>
        <nav aria-label="Navegação principal" className="sticky bottom-0 grid h-16 grid-cols-4 border-t border-borda bg-superficie pb-[env(safe-area-inset-bottom)]">
          <ItemDeNavegacao para="/tecnico/ordens" icone={<IconeMapa aria-hidden />} texto="Ordens" />
          <ItemDeNavegacao para="/tecnico/envios" icone={<CloudUpload aria-hidden />} texto="Envios" />
          <ItemDeNavegacao para="/conta" icone={<UserRound aria-hidden />} texto="Conta" />
          <BotaoDeSairNoMenu />
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
