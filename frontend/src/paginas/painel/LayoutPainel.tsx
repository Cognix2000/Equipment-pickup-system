import { LogOut } from 'lucide-react'
import { NavLink, Outlet } from 'react-router'
import { Button } from '@/components/ui/button'
import { useEu, type Papel } from '@/features/acesso/acesso'
import { useSair } from '@/features/acesso/useSair'
import { cn } from '@/lib/utils'

const secoes: { para: string; texto: string; papeis: Papel[] }[] = [
  { para: '/financeiro/ordens', texto: 'Ordens', papeis: ['admin', 'financeiro', 'estoque'] },
  { para: '/financeiro/importar', texto: 'Importar', papeis: ['admin', 'financeiro'] },
  { para: '/gestao/usuarios', texto: 'Usuários', papeis: ['admin'] },
]

/** Casca do painel web. No celular, o menu desce para a linha de baixo e rola de lado. */
export function LayoutPainel() {
  const { data: eu } = useEu()
  const sair = useSair()
  const visiveis = secoes.filter((s) => eu && s.papeis.includes(eu.papel))

  return (
    <div className="min-h-dvh bg-fundo pb-10">
      <header className="bg-marinho text-white">
        <div className="mx-auto flex max-w-[1200px] flex-wrap items-center gap-x-8 gap-y-1 px-4 py-2">
          <NavLink to="/" className="flex min-h-12 items-center font-titulo text-xl font-bold whitespace-nowrap text-white no-underline">
            Retirada · Netwise
          </NavLink>
          <nav aria-label="Seções do painel" className="flex min-w-0 grow-[999] basis-[420px] gap-1 overflow-x-auto">
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
            {eu && <span className="text-sm text-texto-claro">{eu.nome}</span>}
            <Button variante="fantasma" tamanho="compacto" className="text-white hover:bg-white/10" onClick={() => void sair()}>
              <LogOut aria-hidden /> Sair
            </Button>
          </div>
        </div>
      </header>
      <main className="mx-auto flex max-w-[1200px] flex-col gap-5 px-4 pt-6">
        <Outlet />
      </main>
    </div>
  )
}
