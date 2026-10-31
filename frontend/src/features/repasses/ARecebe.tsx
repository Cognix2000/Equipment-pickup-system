import { useQuery } from '@tanstack/react-query'
import { Wallet } from 'lucide-react'
import { emReais, meuRepasseQuery } from './repasses'

const formatoDia = new Intl.DateTimeFormat('pt-BR', { day: '2-digit', month: '2-digit', timeZone: 'America/Sao_Paulo' })
const SeteDias = 7 * 24 * 60 * 60 * 1000

/**
 * Na tela do técnico: quanto ele tem a receber pelas retiradas conferidas e, se o financeiro registrou um pagamento
 * nos últimos 7 dias, o aviso desse pagamento. Se a consulta falhar, some: a lista de ordens é o principal.
 */
export function ARecebe() {
  // A referência de "agora" é a hora da consulta: o componente continua puro e a conta não muda a cada render.
  const { data, dataUpdatedAt } = useQuery(meuRepasseQuery)
  if (!data) return null

  const { aReceber, ultimosPagamentos, valorPorRetiradaCentavos } = data
  const recente = ultimosPagamentos.find((p) => dataUpdatedAt - new Date(p.pagoEm).getTime() < SeteDias)

  return (
    <section aria-label="Seu repasse" className="flex flex-col gap-1 rounded-xl bg-white/10 px-3 py-2">
      <div className="flex items-center justify-between gap-3">
        <p className="flex min-w-0 flex-col">
          <span className="flex items-center gap-1.5 text-[13px] text-texto-claro">
            <Wallet aria-hidden className="size-4 shrink-0" />A receber
          </span>
          <span className="text-[13px] text-texto-claro">
            {aReceber.quantidade === 1 ? '1 retirada conferida' : `${aReceber.quantidade} retiradas conferidas`} · {emReais(valorPorRetiradaCentavos)} cada
          </span>
        </p>
        <span className="font-titulo text-2xl font-bold whitespace-nowrap text-white tabular-nums">{emReais(aReceber.valorCentavos)}</span>
      </div>
      {recente && (
        <p role="status" className="text-[13px] font-medium text-ciano">
          Pagamento de {emReais(recente.valorCentavos)} registrado pelo financeiro em {formatoDia.format(new Date(recente.pagoEm))}
        </p>
      )}
    </section>
  )
}
