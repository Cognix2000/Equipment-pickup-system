import {
  createColumnHelper,
  createSortedRowModel,
  rowSortingFeature,
  sortFn_alphanumeric,
  sortFn_text,
  tableFeatures,
  useTable,
} from '@tanstack/react-table'
import { ArrowDown, ArrowUp, ArrowUpDown } from 'lucide-react'
import { useMemo, useState } from 'react'
import { Link } from 'react-router'
import { Carregando, FalhaAoCarregar, Vazio } from '@/components/EstadoDaTela'
import { Badge } from '@/components/ui/badge'
import { Card } from '@/components/ui/card'
import { useOrdens } from '@/features/ordens/consultas'
import { descreverPrazo, statusComEquipamentoNoCliente } from '@/features/ordens/prazo'
import { StatusBadge } from '@/features/ordens/StatusBadge'
import { caixaDeTitulo, resumoDosItens, rotuloDoStatus, type Ordem } from '@/features/ordens/tipos'
import { cn } from '@/lib/utils'

// TanStack Table v9: recursos e colunas no escopo do módulo, para não recriar a cada render.
const recursos = tableFeatures({
  rowSortingFeature,
  sortedRowModel: createSortedRowModel(),
  sortFns: { alphanumeric: sortFn_alphanumeric, text: sortFn_text },
})
const coluna = createColumnHelper<typeof recursos, Ordem>()
const colunas = coluna.columns([
  coluna.accessor((o) => o.numeroOs ?? 0, {
    id: 'os',
    header: 'OS',
    cell: (c) => (
      <Link to={`/financeiro/ordens/${c.row.original.id}`} className="font-semibold text-azul">
        {c.getValue() || 's/ nº'}
      </Link>
    ),
  }),
  coluna.accessor('cliente', { header: 'Cliente', sortFn: 'text' }),
  coluna.accessor((o) => o.endereco.bairro, { id: 'bairro', header: 'Bairro', sortFn: 'text', cell: (c) => caixaDeTitulo(c.getValue()) }),
  coluna.accessor((o) => resumoDosItens(o), { id: 'itens', header: 'Itens', enableSorting: false }),
  coluna.accessor((o) => rotuloDoStatus[o.status].texto, {
    id: 'status',
    header: 'Situação',
    sortFn: 'text',
    cell: (c) => (
      <span className="flex flex-wrap gap-1.5">
        <StatusBadge status={c.row.original.status} />
        <Alertas ordem={c.row.original} />
      </span>
    ),
  }),
  coluna.accessor('carenciaAte', {
    header: 'Prazo',
    sortFn: 'alphanumeric',
    cell: (c) => <TextoDoPrazo ordem={c.row.original} />,
  }),
])

/** O que pede uma ação do financeiro: dar baixa no HubSoft (RF19) ou contatar quem passou do prazo sem contato (RN09). */
function Alertas({ ordem }: { ordem: Ordem }) {
  return (
    <>
      {ordem.aguardaBaixaNoHubsoft && <Badge tom="alerta">Dar baixa</Badge>}
      {ordem.semContatoNoPrazo && <Badge tom="erro">Sem contato</Badge>}
    </>
  )
}

function TextoDoPrazo({ ordem }: { ordem: Ordem }) {
  if (!statusComEquipamentoNoCliente.has(ordem.status)) return <span className="text-texto-suave">Equipamento recolhido ou ordem encerrada</span>
  const prazo = descreverPrazo(ordem.carenciaAte)
  return <span className={cn(prazo.vencido && 'font-medium text-alerta')}>{prazo.texto}</span>
}

type Recorte = 'todas' | 'baixa' | 'sem_contato' | 'nova' | 'disponivel' | 'atribuida'
const recortes: { id: Recorte; texto: string; filtra: (o: Ordem) => boolean }[] = [
  { id: 'todas', texto: 'Todas', filtra: () => true },
  { id: 'baixa', texto: 'Para dar baixa', filtra: (o) => o.aguardaBaixaNoHubsoft },
  // RN09: sem nenhuma tentativa, a ordem não encerra sozinha depois do prazo; alguém precisa ligar ou mandar o aviso.
  { id: 'sem_contato', texto: 'Sem contato no prazo', filtra: (o) => o.semContatoNoPrazo === true },
  { id: 'nova', texto: 'Em carência', filtra: (o) => o.status === 'nova' },
  { id: 'disponivel', texto: 'Disponíveis', filtra: (o) => o.status === 'disponivel' },
  { id: 'atribuida', texto: 'Atribuídas', filtra: (o) => o.status === 'atribuida' },
]

const semOrdens: Ordem[] = []

export function OrdensFinanceiro() {
  const { data, isPending, isError, error, refetch } = useOrdens()
  const [recorte, setRecorte] = useState<Recorte>('todas')
  const ordens = data ?? semOrdens
  const visiveis = useMemo(() => ordens.filter(recortes.find((r) => r.id === recorte)!.filtra), [ordens, recorte])

  const tabela = useTable({
    features: recursos,
    columns: colunas,
    data: visiveis,
    initialState: { sorting: [{ id: 'carenciaAte', desc: false }] },
  })

  return (
    <>
      <div>
        <h1 className="text-[28px] font-bold text-marinho">Ordens</h1>
        <p className="mt-1 text-[15px] text-texto-suave">Todas as ordens importadas, com a situação de cada uma.</p>
      </div>

      <div role="group" aria-label="Filtrar ordens" className="flex gap-2 overflow-x-auto">
        {recortes.map((r) => {
          const total = ordens.filter(r.filtra).length
          return (
            <button
              key={r.id}
              type="button"
              aria-pressed={recorte === r.id}
              onClick={() => setRecorte(r.id)}
              className={cn(
                'min-h-11 shrink-0 rounded-full px-4 text-sm font-medium',
                recorte === r.id ? 'bg-azul text-white' : 'border border-[#c5cdd6] bg-superficie hover:bg-white/60',
              )}
            >
              {r.texto} · {total}
            </button>
          )
        })}
      </div>

      {isPending && <Carregando rotulo="Carregando ordens" />}
      {isError && <FalhaAoCarregar erro={error} tentarDeNovo={() => void refetch()} />}
      {!isPending && !isError && visiveis.length === 0 && <Vazio titulo="Nenhuma ordem neste filtro" />}

      {visiveis.length > 0 && (
        /* No celular, a tabela vira uma lista de cartões, na mesma ordem. */
        <ul className="flex flex-col gap-3 sm:hidden">
          {tabela.getRowModel().rows.map(({ original: o }) => (
            <li key={o.id} className="flex flex-col gap-1.5 rounded-xl bg-superficie p-4">
              <div className="flex items-start justify-between gap-3">
                <Link to={`/financeiro/ordens/${o.id}`} className="font-semibold text-azul">
                  OS {o.numeroOs ?? 's/ nº'} · {o.cliente}
                </Link>
              </div>
              <span className="flex flex-wrap gap-1.5">
                <StatusBadge status={o.status} />
                <Alertas ordem={o} />
              </span>
              <p className="text-sm text-texto-suave">
                {caixaDeTitulo(o.endereco.bairro)} · {resumoDosItens(o)}
              </p>
              <p className="text-sm">
                <TextoDoPrazo ordem={o} />
              </p>
            </li>
          ))}
        </ul>
      )}

      {visiveis.length > 0 && (
        <Card className="hidden p-0 sm:flex sm:p-0">
          {/* Tabela larga: em tela estreita, rola de lado dentro da caixa, nunca a página. */}
          <div className="overflow-x-auto">
            <table className="w-full min-w-[760px] border-collapse text-sm">
              <thead>
                {tabela.getHeaderGroups().map((grupo) => (
                  <tr key={grupo.id} className="bg-fundo text-left">
                    {grupo.headers.map((h) => {
                      const ordenado = h.column.getIsSorted()
                      return (
                        <th
                          key={h.id}
                          scope="col"
                          aria-sort={ordenado === 'asc' ? 'ascending' : ordenado === 'desc' ? 'descending' : undefined}
                          className="px-3 py-2.5 font-semibold text-texto-suave first:rounded-tl-xl last:rounded-tr-xl"
                        >
                          {h.column.getCanSort() ? (
                            <button type="button" onClick={h.column.getToggleSortingHandler()} className="inline-flex min-h-8 items-center gap-1 hover:text-marinho">
                              <tabela.FlexRender header={h} />
                              {ordenado === 'asc' ? (
                                <ArrowUp aria-hidden className="size-4" />
                              ) : ordenado === 'desc' ? (
                                <ArrowDown aria-hidden className="size-4" />
                              ) : (
                                <ArrowUpDown aria-hidden className="size-4 opacity-40" />
                              )}
                            </button>
                          ) : (
                            <tabela.FlexRender header={h} />
                          )}
                        </th>
                      )
                    })}
                  </tr>
                ))}
              </thead>
              <tbody>
                {tabela.getRowModel().rows.map((linha) => (
                  <tr key={linha.id} className="border-t border-borda">
                    {linha.getAllCells().map((celula) => (
                      <td key={celula.id} className="px-3 py-3 align-top">
                        <tabela.FlexRender cell={celula} />
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Card>
      )}
    </>
  )
}
