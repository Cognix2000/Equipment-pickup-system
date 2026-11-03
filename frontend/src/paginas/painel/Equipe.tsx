import { useQuery } from '@tanstack/react-query'
import { MapPin } from 'lucide-react'
import { lazy, Suspense, useCallback, useMemo } from 'react'
import { Carregando, FalhaAoCarregar, Vazio } from '@/components/EstadoDaTela'
import { descreverSinal, equipeQuery } from '@/features/mapa/localizacao'
import { cn } from '@/lib/utils'

// O MapLibre é a parte mais pesada: carrega depois da lista.
const MapaDaEquipe = lazy(() => import('@/features/mapa/MapaDaEquipe'))

/**
 * Gestor acompanha a equipe em campo: a última posição de cada técnico (CLT ou PJ) e quantas ordens estão com ele.
 * O técnico só aparece no mapa com o app aberto; não existe trajeto guardado.
 */
export function Equipe() {
  // A referência de "agora" é a hora da consulta: a tela continua pura e atualiza junto com os dados (15 s).
  const { data, dataUpdatedAt, isPending, isError, error, refetch } = useQuery(equipeQuery)

  const tecnicos = useMemo(
    () =>
      (data ?? [])
        .map((t) => ({ ...t, sinal: descreverSinal(t.registradaEm, dataUpdatedAt) }))
        .sort((a, b) => Number(b.sinal.recente) - Number(a.sinal.recente) || (b.registradaEm ?? '').localeCompare(a.registradaEm ?? '') || a.nome.localeCompare(b.nome, 'pt-BR')),
    [data, dataUpdatedAt],
  )
  const noMapa = useMemo(
    () =>
      tecnicos.flatMap((t) => (t.latitude !== null && t.longitude !== null ? [{ ...t, latitude: t.latitude, longitude: t.longitude, recente: t.sinal.recente }] : [])),
    [tecnicos],
  )
  const emCampo = tecnicos.filter((t) => t.sinal.recente).length

  const mostrarNaLista = useCallback((id: string) => {
    const item = document.getElementById(`tecnico-${id}`)
    item?.scrollIntoView({ block: 'nearest', behavior: 'smooth' })
    item?.focus({ preventScroll: true })
  }, [])

  return (
    <>
      <div>
        <h1 className="text-[28px] font-bold text-marinho">Equipe em campo</h1>
        <p className="mt-1 text-[15px] text-texto-suave">
          Onde cada técnico estava da última vez que o app dele estava aberto. Atualiza sozinho a cada 15 segundos.
        </p>
      </div>

      {isPending && <Carregando rotulo="Carregando a equipe" />}
      {isError && <FalhaAoCarregar erro={error} tentarDeNovo={() => void refetch()} />}
      {data?.length === 0 && <Vazio titulo="Nenhum técnico ainda">Quando houver técnico cadastrado, ele aparece aqui.</Vazio>}

      {data && data.length > 0 && (
        <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_360px]">
          <Suspense fallback={<div className="h-[360px] rounded-xl bg-mapa lg:h-[560px]" />}>
            <MapaDaEquipe tecnicos={noMapa} aoEscolher={mostrarNaLista} className="h-[360px] w-full overflow-hidden rounded-xl lg:h-[560px]" />
          </Suspense>

          <section aria-labelledby="titulo-da-equipe" className="flex flex-col gap-2">
            <h2 id="titulo-da-equipe" className="text-[15px] font-semibold text-marinho">
              {emCampo === 1 ? '1 técnico em campo agora' : `${emCampo} técnicos em campo agora`} · {tecnicos.length} no total
            </h2>
            <ul className="flex flex-col gap-2">
              {tecnicos.map((t) => (
                <li
                  key={t.id}
                  id={`tecnico-${t.id}`}
                  tabIndex={-1}
                  className="flex items-center justify-between gap-3 rounded-xl bg-superficie p-3.5 outline-offset-2"
                >
                  <span className="flex min-w-0 items-center gap-3">
                    <span
                      aria-hidden
                      className={cn('grid size-9 shrink-0 place-items-center rounded-full', t.sinal.recente ? 'bg-azul text-white' : 'bg-desabilitado text-marinho')}
                    >
                      <MapPin className="size-[18px]" />
                    </span>
                    <span className="min-w-0">
                      <span className="block truncate font-semibold text-marinho">{t.nome}</span>
                      <span className="block text-[13px] text-texto-suave">
                        {t.ordensComEle === 1 ? '1 ordem com ele' : `${t.ordensComEle} ordens com ele`}
                      </span>
                    </span>
                  </span>
                  <span className={cn('shrink-0 text-right text-[13px]', t.sinal.recente ? 'font-semibold text-sucesso' : 'text-texto-suave')}>
                    {t.sinal.texto}
                    {t.precisaoM !== null && t.sinal.recente && <span className="block font-normal text-texto-suave">±{Math.round(t.precisaoM)} m</span>}
                  </span>
                </li>
              ))}
            </ul>
          </section>
        </div>
      )}
    </>
  )
}
