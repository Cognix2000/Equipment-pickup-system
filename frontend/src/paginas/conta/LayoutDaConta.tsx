import { useEu } from '@/features/acesso/acesso'
import { LayoutPainel } from '@/paginas/painel/LayoutPainel'
import { LayoutTecnico } from '@/paginas/tecnico/LayoutTecnico'

/** Minha conta abre dentro da casca de cada um: a do celular para o técnico, a do painel para os outros. */
export function LayoutDaConta() {
  const { data: eu } = useEu()
  return eu?.papel === 'tecnico' ? <LayoutTecnico /> : <LayoutPainel />
}
