import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CircleCheck, Search, Store, TriangleAlert } from 'lucide-react'
import { useId, useState, type ReactNode } from 'react'
import { Carregando, FalhaAoCarregar, Vazio } from '@/components/EstadoDaTela'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardTitle } from '@/components/ui/card'
import { comandos, normalizarSerie, novaChave } from '@/features/ordens/comandos'
import { ordensQuery, useOrdens } from '@/features/ordens/consultas'
import { StatusBadge } from '@/features/ordens/StatusBadge'
import { caixaDeTitulo, enderecoCurto, resumoDosItens, rotuloDoTipo, type Equipamento, type Ordem } from '@/features/ordens/tipos'
import { ErroDaApi } from '@/lib/api'
import { cn } from '@/lib/utils'

const mensagem = (erro: unknown) => (erro instanceof ErroDaApi ? erro.message : 'Sem resposta do servidor. Confira a conexão e tente de novo.')

/** Devolução na loja vale até depois de encerrada sem retirada (RN13); não vale para ordem já fechada. */
const aceitaDevolucao = (o: Ordem) => !['conferida', 'cancelada', 'devolvida_na_loja'].includes(o.status)

/**
 * UC06 e RF14: o estoque acha a ordem pelo protocolo, nº da OS ou série, lê a série de cada item
 * e fecha como Conferida. Divergência ou item faltando deixa a ordem aberta (RN08). RF15: devolução na loja.
 */
export function Conferir() {
  const idBusca = useId()
  const [texto, setTexto] = useState('')
  const [busca, setBusca] = useState<string>()
  // Guarda a ordem escolhida: depois de conferida, ela sai da lista de espera, mas o resultado continua na tela.
  const [escolhida, setEscolhida] = useState<Ordem>()
  const aguardando = useOrdens({ status: 'concluida' })
  const resultado = useQuery({ ...ordensQuery({ busca }), enabled: !!busca })

  // Uma ordem só na busca já vem aberta; com várias, a pessoa escolhe.
  const encontradas = busca ? (resultado.data ?? []) : []
  const selecionada = escolhida ?? (encontradas.length === 1 ? encontradas[0] : undefined)

  return (
    <>
      <div>
        <h1 className="text-[28px] font-bold text-marinho">Conferir no estoque</h1>
        <p className="mt-1 text-[15px] text-texto-suave">Galpão do estoque, junto ao data center. Leia a série de cada item que chegou.</p>
      </div>

      <div className="grid items-start gap-5 lg:grid-cols-[minmax(0,1fr)_340px]">
        <div className="flex min-w-0 flex-col gap-4">
          <Card>
            <form
              role="search"
              className="flex flex-col gap-2 sm:flex-row sm:items-end"
              onSubmit={(e) => {
                e.preventDefault()
                setEscolhida(undefined)
                setBusca(texto.trim() || undefined)
              }}
            >
              <div className="flex min-w-0 grow flex-col gap-1.5">
                <label htmlFor={idBusca} className="text-sm font-medium">
                  Protocolo, número da OS ou série de um item
                </label>
                <input
                  id={idBusca}
                  value={texto}
                  onChange={(e) => setTexto(e.target.value)}
                  autoComplete="off"
                  spellCheck={false}
                  className="min-h-12 w-full rounded-[10px] border border-borda-forte bg-superficie px-3 text-base"
                />
              </div>
              <Button type="submit">
                <Search aria-hidden /> Buscar
              </Button>
            </form>
          </Card>

          {busca && resultado.isPending && <Carregando linhas={1} rotulo="Buscando a ordem" />}
          {busca && resultado.isError && <FalhaAoCarregar erro={resultado.error} tentarDeNovo={() => void resultado.refetch()} />}
          {busca && resultado.data?.length === 0 && <Vazio titulo="Nenhuma ordem encontrada">Confira o número e busque de novo.</Vazio>}
          {encontradas.length > 1 && !selecionada && (
            <Card>
              <CardTitle className="text-base sm:text-base">{encontradas.length} ordens encontradas</CardTitle>
              <ListaDeOrdens ordens={encontradas} aoEscolher={setEscolhida} />
            </Card>
          )}

          {selecionada && <PainelDaOrdem key={selecionada.id} ordem={selecionada} />}
        </div>

        <Card className="gap-3">
          <CardTitle className="text-base sm:text-base">Aguardando conferência · {aguardando.data?.length ?? 0}</CardTitle>
          {aguardando.isPending && <Carregando linhas={2} rotulo="Carregando as entregas" />}
          {aguardando.isError && <FalhaAoCarregar erro={aguardando.error} tentarDeNovo={() => void aguardando.refetch()} />}
          {aguardando.data?.length === 0 && <p className="text-sm text-texto-suave">Nada aguardando conferência.</p>}
          {aguardando.data && aguardando.data.length > 0 && (
            <ListaDeOrdens ordens={aguardando.data} selecionada={selecionada?.id} aoEscolher={setEscolhida} />
          )}
        </Card>
      </div>
    </>
  )
}

function ListaDeOrdens({ ordens, selecionada, aoEscolher }: { ordens: Ordem[]; selecionada?: string; aoEscolher: (o: Ordem) => void }) {
  return (
    <ul className="flex flex-col gap-2">
      {ordens.map((o) => (
        <li key={o.id}>
          <button
            type="button"
            onClick={() => aoEscolher(o)}
            aria-current={o.id === selecionada ? 'true' : undefined}
            className={cn(
              'flex w-full flex-col items-start gap-0.5 rounded-[10px] border px-3 py-2.5 text-left',
              o.id === selecionada ? 'border-azul bg-info-fundo' : 'border-borda hover:bg-fundo',
            )}
          >
            <span className="font-semibold text-marinho">
              OS {o.numeroOs ?? 's/ nº'} · {o.cliente}
            </span>
            <span className="text-[13px] text-texto-suave">
              {resumoDosItens(o) || 'sem itens'} · {caixaDeTitulo(o.endereco.bairro)}
            </span>
          </button>
        </li>
      ))}
    </ul>
  )
}

type Leitura = { serie: string; naoVeio: boolean }

function PainelDaOrdem({ ordem }: { ordem: Ordem }) {
  const clienteDeConsultas = useQueryClient()
  const [leituras, setLeituras] = useState<Record<string, Leitura>>({})
  // Uma chave por ação nesta ordem; a mesma em todas as tentativas (ADR-009).
  const [chaveDaConferencia] = useState(novaChave)
  const [chaveDaDevolucao] = useState(novaChave)
  const [confirmandoDevolucao, setConfirmandoDevolucao] = useState(false)

  const patrimoniais = ordem.equipamentos.filter((e) => e.numeroSerie !== null)
  const leitura = (e: Equipamento): Leitura => leituras[e.id] ?? { serie: '', naoVeio: false }
  const lida = (e: Equipamento) => normalizarSerie(leitura(e).serie)
  const confere = (e: Equipamento) => !leitura(e).naoVeio && lida(e) === normalizarSerie(e.numeroSerie ?? '')
  const faltaLer = patrimoniais.filter((e) => !leitura(e).naoVeio && lida(e).length === 0).length
  const divergencias = patrimoniais.filter((e) => (leitura(e).naoVeio || lida(e).length > 0) && !confere(e)).length

  const atualizar = () => clienteDeConsultas.invalidateQueries({ queryKey: ['ordens'] })
  const conferir = useMutation({
    mutationFn: () =>
      comandos.conferir(
        ordem.id,
        ordem.equipamentos.map((e) => ({ equipamentoId: e.id, numeroSerie: e.numeroSerie === null || leitura(e).naoVeio ? null : leitura(e).serie })),
        chaveDaConferencia,
      ),
    onSuccess: atualizar,
  })
  const devolver = useMutation({ mutationFn: () => comandos.registrarDevolucaoNaLoja(ordem.id, chaveDaDevolucao), onSuccess: atualizar })

  const conferida = conferir.data
  const devolvida = devolver.data

  return (
    <Card className="gap-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <CardTitle className="text-lg sm:text-lg">
            OS {ordem.numeroOs ?? 's/ nº'} · {ordem.cliente}
          </CardTitle>
          <p className="text-sm text-texto-suave">
            {caixaDeTitulo(enderecoCurto(ordem))} · {caixaDeTitulo(ordem.endereco.bairro)}
          </p>
        </div>
        <StatusBadge status={(devolvida ?? conferida ?? ordem).status} />
      </div>

      {conferida?.status === 'conferida' && (
        <Resultado tom="sucesso">Ordem conferida. O financeiro recebeu o aviso para dar baixa no HubSoft.</Resultado>
      )}
      {conferida && conferida.status !== 'conferida' && (
        <Resultado tom="alerta">
          Divergência registrada: a ordem continua aberta.{' '}
          {conferida.equipamentos
            .filter((e) => e.situacao === 'divergente')
            .map((e) => rotuloDoTipo[e.tipo])
            .join(', ')}{' '}
          {conferida.equipamentos.filter((e) => e.situacao === 'divergente').length === 1 ? 'ficou pendente.' : 'ficaram pendentes.'}
        </Resultado>
      )}
      {devolvida && <Resultado tom="sucesso">Devolução na loja registrada. A ordem foi encerrada e saiu da lista de cobrança.</Resultado>}

      {!conferida && !devolvida && ordem.status === 'concluida' && (
        <form
          noValidate
          className="flex flex-col gap-3"
          onSubmit={(e) => {
            e.preventDefault()
            conferir.mutate()
          }}
        >
          {ordem.equipamentos.map((e, i) => (
            <ItemDaConferencia
              key={e.id}
              posicao={i + 1}
              equipamento={e}
              leitura={leitura(e)}
              confere={confere(e)}
              aoMudar={(l) => setLeituras((atual) => ({ ...atual, [e.id]: l }))}
            />
          ))}
          <p className="text-[13px] text-texto-suave">
            Ao fechar, a ordem vira Conferida e o financeiro recebe o aviso para dar baixa no HubSoft. Com divergência ou item faltando, ela continua
            aberta.
          </p>
          {conferir.isError && (
            <p role="alert" className="rounded-[10px] bg-erro-fundo px-3.5 py-2.5 text-sm text-erro">
              {mensagem(conferir.error)}
            </p>
          )}
          <Button type="submit" className="self-start" disabled={faltaLer > 0 || conferir.isPending}>
            {conferir.isPending
              ? 'Registrando'
              : faltaLer > 0
                ? `Falta ler ${faltaLer} ${faltaLer === 1 ? 'item' : 'itens'}`
                : divergencias > 0
                  ? `Registrar ${divergencias === 1 ? '1 divergência' : `${divergencias} divergências`}`
                  : 'Fechar como Conferida'}
          </Button>
        </form>
      )}

      {!conferida && !devolvida && ordem.status !== 'concluida' && (
        <p className="rounded-[10px] bg-fundo px-3.5 py-2.5 text-sm text-texto-suave">
          Só ordem com retirada registrada é conferida. Esta ainda não chegou ao estoque.
        </p>
      )}

      {!devolvida && conferida?.status !== 'conferida' && aceitaDevolucao(ordem) && (
        <div className="flex flex-col gap-2 border-t border-borda pt-4">
          <p className="flex items-center gap-2 font-semibold text-marinho">
            <Store aria-hidden className="size-5" /> Devolução na loja
          </p>
          <p className="text-sm text-texto-suave">O cliente levou o aparelho na loja? Registre para encerrar a ordem e tirá-la da cobrança.</p>
          {devolver.isError && (
            <p role="alert" className="rounded-[10px] bg-erro-fundo px-3.5 py-2.5 text-sm text-erro">
              {mensagem(devolver.error)}
            </p>
          )}
          {confirmandoDevolucao ? (
            <div className="flex flex-wrap gap-2">
              <Button variante="perigo" tamanho="compacto" disabled={devolver.isPending} onClick={() => devolver.mutate()}>
                {devolver.isPending ? 'Registrando' : 'Confirmar devolução'}
              </Button>
              <Button variante="neutro" tamanho="compacto" onClick={() => setConfirmandoDevolucao(false)}>
                Cancelar
              </Button>
            </div>
          ) : (
            <Button variante="neutro" tamanho="compacto" className="self-start" onClick={() => setConfirmandoDevolucao(true)}>
              Registrar devolução
            </Button>
          )}
        </div>
      )}
    </Card>
  )
}

function ItemDaConferencia({
  posicao,
  equipamento: e,
  leitura,
  confere,
  aoMudar,
}: {
  posicao: number
  equipamento: Equipamento
  leitura: Leitura
  confere: boolean
  aoMudar: (l: Leitura) => void
}) {
  const idCampo = useId()
  const preenchida = normalizarSerie(leitura.serie).length > 0

  return (
    <div className="flex flex-col gap-2 rounded-xl border border-borda p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="font-semibold">
          {posicao} · {rotuloDoTipo[e.tipo]} · {e.modelo}
        </p>
        {e.numeroSerie === null ? (
          <Badge tom="neutro">Sem série</Badge>
        ) : leitura.naoVeio ? (
          <Badge tom="alerta">Fica pendente com o técnico</Badge>
        ) : preenchida ? (
          <Badge tom={confere ? 'sucesso' : 'erro'}>{confere ? 'Confere' : 'Divergência'}</Badge>
        ) : null}
      </div>

      {e.numeroSerie === null ? (
        <p className="text-sm text-texto-suave">Acessório sem série: confira junto com o kit.</p>
      ) : (
        <>
          <label htmlFor={idCampo} className="text-sm font-medium">
            Série lida <span className="font-normal text-texto-suave">(esperada: {e.numeroSerie})</span>
          </label>
          <input
            id={idCampo}
            value={leitura.serie}
            disabled={leitura.naoVeio}
            onChange={(ev) => aoMudar({ ...leitura, serie: ev.target.value })}
            autoComplete="off"
            spellCheck={false}
            className="min-h-12 w-full rounded-[10px] border border-borda-forte bg-superficie px-3 font-titulo text-base tracking-wide uppercase disabled:bg-fundo"
          />
          {preenchida && !confere && !leitura.naoVeio && (
            <p className="flex gap-1.5 text-sm text-erro">
              <TriangleAlert aria-hidden className="mt-0.5 size-4 shrink-0" />
              Essa série não é da ordem. Leia de novo; se confirmar, a divergência é registrada e a ordem não fecha.
            </p>
          )}
          <label className="flex min-h-11 items-center gap-3 text-sm">
            <input
              type="checkbox"
              checked={leitura.naoVeio}
              onChange={(ev) => aoMudar({ serie: '', naoVeio: ev.target.checked })}
              className="size-5 accent-azul"
            />
            Não veio: deixar pendente com o técnico
          </label>
        </>
      )}
    </div>
  )
}

function Resultado({ tom, children }: { tom: 'sucesso' | 'alerta'; children: ReactNode }) {
  return (
    <p
      role="status"
      className={cn(
        'flex items-start gap-2 rounded-[10px] px-3.5 py-2.5 text-sm font-medium',
        tom === 'sucesso' ? 'bg-sucesso-fundo text-sucesso' : 'bg-alerta-fundo text-alerta',
      )}
    >
      {tom === 'sucesso' ? <CircleCheck aria-hidden className="mt-0.5 size-4 shrink-0" /> : <TriangleAlert aria-hidden className="mt-0.5 size-4 shrink-0" />}
      {children}
    </p>
  )
}
