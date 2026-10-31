import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ChevronDown } from 'lucide-react'
import { useId, useState } from 'react'
import { Carregando, FalhaAoCarregar, Vazio } from '@/components/EstadoDaTela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { classeDoInput } from '@/components/ui/input'
import {
  detalheDoRepasseQuery,
  emReais,
  registrarPagamento,
  repassesQuery,
  type TecnicoNaLista,
} from '@/features/repasses/repasses'
import { ErroDaApi } from '@/lib/api'
import { cn } from '@/lib/utils'
import { AvisoDeErro, Campo } from '@/paginas/acesso/LayoutDeAcesso'

const formatoData = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeZone: 'America/Sao_Paulo' })
const formatoDataHora = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short', timeZone: 'America/Sao_Paulo' })

/**
 * Repasse ao técnico (financeiro e gestor): quanto cada um tem a receber pelas retiradas conferidas e o registro
 * do pagamento. O sistema não transfere dinheiro: o financeiro paga por fora (folha ou PIX) e registra aqui.
 */
export function Repasses() {
  const { data, isPending, isError, error, refetch } = useQuery(repassesQuery)
  const total = (data ?? []).reduce((soma, t) => soma + t.valorCentavos, 0)

  return (
    <>
      <div>
        <h1 className="text-[28px] font-bold text-marinho">Repasses</h1>
        <p className="mt-1 text-[15px] text-texto-suave">
          Cada retirada conferida pelo estoque entra aqui. Pague o técnico e registre o pagamento: ele vê na tela dele.
        </p>
      </div>

      {data && data.length > 0 && (
        <p className="text-[15px]">
          Total a pagar: <strong className="font-titulo text-lg text-marinho tabular-nums">{emReais(total)}</strong>
        </p>
      )}
      {isPending && <Carregando rotulo="Carregando os repasses" />}
      {isError && <FalhaAoCarregar erro={error} tentarDeNovo={() => void refetch()} />}
      {data?.length === 0 && <Vazio titulo="Nenhum técnico ainda">Quando houver técnico cadastrado, ele aparece aqui.</Vazio>}
      <div className="flex max-w-[860px] flex-col gap-3">
        {data?.map((t) => (
          <LinhaDoTecnico key={t.id} tecnico={t} />
        ))}
      </div>
    </>
  )
}

function LinhaDoTecnico({ tecnico: t }: { tecnico: TecnicoNaLista }) {
  const [aberto, setAberto] = useState(false)
  const idDoDetalhe = useId()

  return (
    <article aria-label={t.nome} className="flex flex-col rounded-xl bg-superficie">
      <button
        type="button"
        aria-expanded={aberto}
        aria-controls={idDoDetalhe}
        onClick={() => setAberto((a) => !a)}
        className="flex min-h-16 flex-wrap items-center justify-between gap-x-4 gap-y-1 rounded-xl p-4 text-left"
      >
        <span className="min-w-0">
          <span className="flex flex-wrap items-center gap-2">
            <span className="text-base font-semibold text-marinho">{t.nome}</span>
            {t.situacao !== 'ativo' && <Badge tom="erro">Revogado</Badge>}
          </span>
          <span className="block text-[13px] text-texto-suave">
            {t.quantidade === 1 ? '1 retirada conferida a pagar' : `${t.quantidade} retiradas conferidas a pagar`}
            {t.ultimoPagamentoEm ? ` · último pagamento em ${formatoData.format(new Date(t.ultimoPagamentoEm))}` : ''}
          </span>
        </span>
        <span className="flex items-center gap-2">
          <span className={cn('font-titulo text-xl font-bold tabular-nums', t.valorCentavos > 0 ? 'text-marinho' : 'text-texto-suave')}>
            {emReais(t.valorCentavos)}
          </span>
          <ChevronDown aria-hidden className={cn('size-5 text-azul transition-transform motion-reduce:transition-none', aberto && 'rotate-180')} />
        </span>
      </button>
      {aberto && (
        <div id={idDoDetalhe} className="border-t border-borda p-4">
          <DetalheDoTecnico tecnicoId={t.id} />
        </div>
      )}
    </article>
  )
}

function DetalheDoTecnico({ tecnicoId }: { tecnicoId: string }) {
  const { data, isPending, isError, error, refetch } = useQuery(detalheDoRepasseQuery(tecnicoId))
  if (isPending) return <Carregando linhas={1} rotulo="Carregando as retiradas" />
  if (isError) return <FalhaAoCarregar erro={error} tentarDeNovo={() => void refetch()} />

  return (
    <div className="flex flex-col gap-5">
      {data.emAberto.length > 0 ? (
        <>
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <caption className="sr-only">Retiradas conferidas a pagar</caption>
              <thead className="text-texto-suave">
                <tr>
                  <th scope="col" className="py-2 pr-3 font-medium">Conferida em</th>
                  <th scope="col" className="py-2 pr-3 font-medium">OS</th>
                  <th scope="col" className="py-2 pr-3 font-medium">Bairro</th>
                  <th scope="col" className="py-2 text-right font-medium">Valor</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-borda">
                {data.emAberto.map((c) => (
                  <tr key={c.id}>
                    <td className="py-2 pr-3 whitespace-nowrap">{formatoDataHora.format(new Date(c.creditadoEm))}</td>
                    <td className="py-2 pr-3">{c.numeroOs ?? '—'}</td>
                    <td className="py-2 pr-3">{c.bairro ?? '—'}</td>
                    <td className="py-2 text-right tabular-nums">{emReais(c.valorCentavos)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <RegistroDePagamento tecnicoId={tecnicoId} valorCentavos={data.valorCentavos} quantidade={data.quantidade} />
        </>
      ) : (
        <p className="text-[15px] text-texto-suave">Nada a pagar agora.</p>
      )}

      {data.pagamentos.length > 0 && (
        <section aria-label="Pagamentos registrados" className="flex flex-col gap-2">
          <h3 className="text-sm font-semibold text-marinho">Pagamentos registrados</h3>
          <ul className="flex flex-col gap-1 text-sm">
            {data.pagamentos.map((p) => (
              <li key={p.id} className="flex flex-wrap justify-between gap-x-3">
                <span>
                  {formatoData.format(new Date(p.pagoEm))} · {p.quantidade === 1 ? '1 retirada' : `${p.quantidade} retiradas`}
                  {p.observacao ? ` · ${p.observacao}` : ''}
                </span>
                <span className="tabular-nums">{emReais(p.valorCentavos)}</span>
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  )
}

/** Dois passos: primeiro pede a confirmação, porque o registro não se desfaz. */
function RegistroDePagamento({ tecnicoId, valorCentavos, quantidade }: { tecnicoId: string; valorCentavos: number; quantidade: number }) {
  const clienteDeConsultas = useQueryClient()
  const [observacao, setObservacao] = useState('')
  const [confirmando, setConfirmando] = useState(false)
  const idDaObservacao = useId()
  const pagar = useMutation({
    mutationFn: () => registrarPagamento(tecnicoId, valorCentavos, observacao.trim()),
    onSettled: () => {
      setConfirmando(false)
      return clienteDeConsultas.invalidateQueries({ queryKey: ['repasses', 'tecnicos'] })
    },
    onSuccess: () => setObservacao(''),
  })
  const erro = pagar.error ? (pagar.error instanceof ErroDaApi ? pagar.error.message : 'Sem resposta do servidor. Tente de novo.') : undefined

  return (
    <div className="flex flex-col gap-3 rounded-[10px] bg-fundo p-3">
      <AvisoDeErro mensagem={erro} />
      <Campo id={idDaObservacao} rotulo="Observação (opcional)">
        <input
          id={idDaObservacao}
          maxLength={200}
          placeholder="Ex.: PIX de 31/10, folha de novembro"
          className={classeDoInput}
          value={observacao}
          onChange={(e) => setObservacao(e.target.value)}
        />
      </Campo>
      {confirmando ? (
        <div className="flex flex-col gap-2">
          <p className="text-[15px] font-medium">
            Você já pagou {emReais(valorCentavos)} ({quantidade === 1 ? '1 retirada' : `${quantidade} retiradas`}) a este técnico?
          </p>
          <div className="flex flex-wrap gap-2">
            <Button tamanho="compacto" disabled={pagar.isPending} onClick={() => pagar.mutate()}>
              {pagar.isPending ? 'Registrando' : 'Sim, registrar o pagamento'}
            </Button>
            <Button variante="fantasma" tamanho="compacto" onClick={() => setConfirmando(false)}>
              Voltar
            </Button>
          </div>
        </div>
      ) : (
        <Button className="self-start" onClick={() => setConfirmando(true)}>
          Registrar pagamento de {emReais(valorCentavos)}
        </Button>
      )}
    </div>
  )
}
