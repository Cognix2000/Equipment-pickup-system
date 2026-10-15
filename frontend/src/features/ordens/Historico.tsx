import { Card, CardTitle } from '@/components/ui/card'
import { descreverEvento, quando } from './historico'
import type { EventoDaOrdem } from './tipos'

/** RF16: o que aconteceu com a ordem, do mais novo para o mais antigo. Quem fez aparece só pelo papel. */
export function Historico({ eventos }: { eventos: EventoDaOrdem[] | null | undefined }) {
  if (!eventos || eventos.length === 0) return null
  return (
    <Card>
      <CardTitle className="text-base sm:text-base">Histórico</CardTitle>
      <ol className="flex flex-col gap-1.5 text-[13px]">
        {eventos.map((e) => (
          <li key={`${e.tipo}-${e.ocorridoEm}`}>
            <span className="text-texto-suave tabular-nums">{quando(e.ocorridoEm)}</span> · {descreverEvento(e)}
          </li>
        ))}
      </ol>
    </Card>
  )
}
