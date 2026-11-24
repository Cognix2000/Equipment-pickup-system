import { LogOut, UserRound } from 'lucide-react'
import { NavLink, Outlet } from 'react-router'
import { useEu, type Papel } from '@/features/acesso/acesso'
import { BotaoDeSair } from '@/features/acesso/BotaoDeSair'
import { cn } from '@/lib/utils'

const secoes: { para: string; texto: string; papeis: Papel[] }[] = [
  { para: '/financeiro/ordens', texto: 'Ordens', papeis: ['admin', 'financeiro', 'estoque'] },
  { para: '/financeiro/nova-ordem', texto: 'Cadastrar ordem', papeis: ['admin', 'financeiro'] },
  { para: '/financeiro/importar', texto: 'Importar', papeis: ['admin', 'financeiro'] },
  { para: '/financeiro/repasses', texto: 'Repasses', papeis: ['admin', 'financeiro', 'estoque'] },
  { para: '/estoque/conferir', texto: 'Conferir', papeis: ['admin', 'estoque'] },
  { para: '/gestao/equipe', texto: 'Equipe', papeis: ['admin'] },
  { para: '/gestao/usuarios', texto: 'Usuários', papeis: ['admin'] },
]

/** Casca do painel web. No celular, o menu desce para a linha de baixo e rola de lado. */
export function LayoutPainel() {
  const { data: eu } = useEu()
  const visiveis = secoes.filter((s) => eu && s.papeis.includes(eu.papel))

  return (
    <div className="min-h-dvh bg-fundo pb-10">
      <header className="bg-marinho text-white">
        <div className="mx-auto flex max-w-[1200px] flex-wrap items-center gap-x-8 gap-y-1 px-4 py-2">
          <NavLink to="/" className="flex min-h-12 items-center font-titulo text-xl font-bold whitespace-nowrap text-white no-underline">
            Retirada · Netwise
          </NavLink>
          <nav aria-label="Seções do painel" className="flex min-w-0 grow-[999] basis-[420px] gap-1 overflow-x-auto [scrollbar-width:none] [-ms-overflow-style:none] [mask-image:linear-gradient(to_right,transparent,black_16px,black_calc(100%-16px),transparent)] [-webkit-mask-image:linear-gradient(to_right,transparent,black_16px,black_calc(100%-16px),transparent)] [&::-webkit-scrollbar]:hidden">
            {visiveis.map((s) => (
              <NavLink
                key={s.para}
                to={s.para}
                className={({ isActive }) =>
                  cn(
                    'flex min-h-11 shrink-0 items-center rounded-lg px-3.5 text-[15px] no-underline',
                    isActive ? 'bg-azul font-semibold text-white' : 'text-texto-claro hover:bg-white/10',
                  )
                }
              >
                {s.texto}
              </NavLink>
            ))}
          </nav>
          <div className="flex items-center gap-1">
            {eu && (
              <NavLink
                to="/conta"
                title="Minha conta"
                className={({ isActive }) =>
                  cn(
                    'flex min-h-11 items-center gap-1.5 rounded-lg px-3 text-sm no-underline [&_svg]:size-[18px]',
                    isActive ? 'bg-azul font-semibold text-white' : 'text-texto-claro hover:bg-white/10',
                  )
                }
              >
                <UserRound aria-hidden /> {eu.nome}
              </NavLink>
            )}
            <BotaoDeSair className="flex min-h-11 items-center gap-1.5 rounded-lg px-3 text-sm text-texto-claro hover:bg-white/10 [&_svg]:size-[18px]">
              <LogOut aria-hidden /> Sair
            </BotaoDeSair>
          </div>
        </div>
      </header>
      <main className="mx-auto flex max-w-[1200px] flex-col gap-5 px-4 pt-6">
        <Outlet />
      </main>
    </div>
  )
}
