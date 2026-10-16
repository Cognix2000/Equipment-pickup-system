import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CircleCheck, MapPin, TriangleAlert } from 'lucide-react'
import { useId, useState, type ReactNode } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { Carregando, FalhaAoCarregar } from '@/components/EstadoDaTela'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { comandos, normalizarSerie, novaChave } from '@/features/ordens/comandos'
import { ordemQuery } from '@/features/ordens/consultas'
import { GpsIndisponivel, obterGps } from '@/features/ordens/gps'
import { comOCliente, devolvidosNaLoja, nomeDoItem, enderecoCurto, rotuloDoTipo, type Equipamento, type Ordem } from '@/features/ordens/tipos'
import { LIMITE_DE_FOTOS, useFotosDaRetirada } from '@/features/ordens/useFotosDaRetirada'
import { ErroDaApi } from '@/lib/api'
import { CabecalhoDaAcao } from './CabecalhoDaAcao'
import { BotaoDeFoto, MiniaturaDaFoto } from './FotosDaRetirada'

const horario = new Intl.DateTimeFormat('pt-BR', { hour: '2-digit', minute: '2-digit', timeZone: 'America/Sao_Paulo' })

/**
 * UC04, RF12 e RN14: o técnico confere a série de cada item, fotografa e confirma; GPS e hora entram sozinhos.
 * RN07: ao abrir, a tela busca a ordem de novo no servidor; cancelada, a retirada fica bloqueada.
 */
export function RegistrarRetirada() {
  const { id = '' } = useParams()
  const consulta = useQuery({ ...ordemQuery(id), staleTime: 0, refetchOnMount: 'always' })
  const { data: ordem, isPending, isError, error, refetch, dataUpdatedAt } = consulta

  return (
    <>
      <CabecalhoDaAcao
        titulo="Registrar retirada"
        subtitulo={ordem ? `OS ${ordem.numeroOs ?? 's/ nº'} · ${enderecoCurto(ordem)}` : undefined}
        voltarPara={`/tecnico/ordens/${id}`}
      />
      <main className="flex flex-1 flex-col gap-3 p-4">
        {isPending && <Carregando linhas={3} rotulo="Conferindo a ordem no servidor" />}
        {isError && <FalhaAoCarregar erro={error} tentarDeNovo={() => void refetch()} />}
        {ordem && ordem.status === 'cancelada' && (
          <Bloqueio texto="Esta ordem foi cancelada: a retirada está bloqueada. Não recolha os equipamentos." voltarPara="/tecnico/ordens" />
        )}
        {ordem && ordem.status !== 'cancelada' && ordem.status !== 'atribuida' && ordem.status !== 'em_rota' && (
          <Bloqueio texto="Esta ordem não está mais aguardando retirada com você." voltarPara="/tecnico/ordens" />
        )}
        {ordem && (ordem.status === 'atribuida' || ordem.status === 'em_rota') && (
          <FormularioDaRetirada ordem={ordem} conferidaAs={horario.format(dataUpdatedAt)} />
        )}
      </main>
    </>
  )
}

function FormularioDaRetirada({ ordem, conferidaAs }: { ordem: Ordem; conferidaAs: string }) {
  const navegar = useNavigate()
  const clienteDeConsultas = useQueryClient()
  const [chave] = useState(novaChave) // a mesma chave em todas as tentativas desta retirada
  const [series, setSeries] = useState<Record<string, string>>({})
  const [recolhidos, setRecolhidos] = useState<Record<string, boolean>>({})

  const fotos = useFotosDaRetirada(ordem.id)
  // RF15: o que o cliente já devolveu na loja não entra na retirada.
  const itens = comOCliente(ordem)
  const devolvidos = devolvidosNaLoja(ordem)

  const temSerie = (e: Equipamento) => e.numeroSerie !== null
  const resolvido = (e: Equipamento) => (temSerie(e) ? (series[e.id] ?? '').trim().length > 0 : recolhidos[e.id] === true)
  const faltando = itens.filter((e) => !resolvido(e)).length
  // RN14: a retirada leva foto dos equipamentos; o item com série diferente leva foto da etiqueta.
  const temFotoGeral = fotos.paraEnviar.some((f) => f.tipo === 'equipamento')
  const semEtiqueta = itens.filter(
    (e) => situacaoDaSerie(e, series[e.id] ?? '') === 'diferente' && !fotos.paraEnviar.some((f) => f.tipo === 'etiqueta_serie' && f.equipamentoId === e.id),
  )
  const cabeMaisFoto = fotos.fotos.length < LIMITE_DE_FOTOS

  const registrar = useMutation({
    mutationFn: async () => {
      const gps = await obterGps()
      const lidos = itens.map((e) => ({ equipamentoId: e.id, numeroSerie: temSerie(e) ? (series[e.id] ?? null) : null }))
      return comandos.registrarRetirada(ordem.id, lidos, gps, fotos.paraEnviar, chave)
    },
    onSuccess: async () => {
      await clienteDeConsultas.invalidateQueries({ queryKey: ['ordens'] })
      navegar('/tecnico/ordens', {
        replace: true,
        state: { aviso: 'Retirada registrada. Leve os equipamentos ao estoque até o fim do dia útil seguinte.' },
      })
    },
  })

  const falha = registrar.error
  const mensagemDeFalha =
    falha instanceof GpsIndisponivel || falha instanceof ErroDaApi ? falha.message : falha ? 'Sem resposta do servidor. Confira o sinal e tente de novo.' : null

  return (
    <form
      noValidate
      onSubmit={(e) => {
        e.preventDefault()
        registrar.mutate()
      }}
      className="flex flex-col gap-3"
    >
      <p className="flex items-center gap-2 rounded-[10px] bg-sucesso-fundo px-3.5 py-2.5 text-sm font-medium text-sucesso">
        <CircleCheck aria-hidden className="size-5 shrink-0" />
        <span>
          Ordem confirmada ativa às <span data-teste="hora">{conferidaAs}</span>
        </span>
      </p>

      {ordem.equipamentos.length === 0 && (
        <p className="rounded-[10px] bg-alerta-fundo px-3.5 py-2.5 text-sm text-alerta">
          A planilha não trouxe os equipamentos desta ordem. Avise o financeiro antes de recolher.
        </p>
      )}

      {devolvidos.length > 0 && (
        <p className="rounded-[10px] bg-info-fundo px-3.5 py-2.5 text-sm text-marinho">
          Já devolvido na loja, não recolha: {devolvidos.map(nomeDoItem).join(', ')}.
        </p>
      )}

      {itens.map((e, i) => {
        const etiqueta = fotos.fotos.find((f) => f.tipo === 'etiqueta_serie' && f.equipamentoId === e.id)
        return (
          <ItemDaRetirada
            key={e.id}
            posicao={i + 1}
            equipamento={e}
            serie={series[e.id] ?? ''}
            recolhido={recolhidos[e.id] === true}
            aoMudarSerie={(valor) => setSeries((s) => ({ ...s, [e.id]: valor }))}
            aoMarcar={(valor) => setRecolhidos((r) => ({ ...r, [e.id]: valor }))}
          >
            {situacaoDaSerie(e, series[e.id] ?? '') === 'diferente' && (
              <div className="flex flex-col gap-2 rounded-[10px] bg-fundo p-3">
                <p className="text-sm">Fotografe a etiqueta: é a prova do SN que você encontrou.</p>
                {etiqueta && (
                  <ul className="grid w-40">
                    <MiniaturaDaFoto
                      foto={etiqueta}
                      descricao={`Foto da etiqueta de ${e.modelo}`}
                      aoRemover={() => fotos.remover(etiqueta.local)}
                      aoTentarDeNovo={() => fotos.tentarDeNovo(etiqueta)}
                    />
                  </ul>
                )}
                <BotaoDeFoto
                  rotulo={etiqueta ? 'Tirar outra foto da etiqueta' : 'Foto da etiqueta'}
                  desabilitado={!etiqueta && !cabeMaisFoto}
                  aoEscolher={(arquivo) => void fotos.adicionar(arquivo, 'etiqueta_serie', e.id)}
                />
              </div>
            )}
          </ItemDaRetirada>
        )
      })}

      <Card className="gap-3">
        <p className="font-semibold">Fotos dos equipamentos</p>
        <p className="text-sm text-texto-suave">Pelo menos uma, com todos os itens recolhidos à vista.</p>
        {fotos.fotos.some((f) => f.tipo === 'equipamento') && (
          <ul className="grid grid-cols-2 gap-3">
            {fotos.fotos
              .filter((f) => f.tipo === 'equipamento')
              .map((f, i) => (
                <MiniaturaDaFoto
                  key={f.local}
                  foto={f}
                  descricao={`Foto dos equipamentos ${i + 1}`}
                  aoRemover={() => fotos.remover(f.local)}
                  aoTentarDeNovo={() => fotos.tentarDeNovo(f)}
                />
              ))}
          </ul>
        )}
        {fotos.erro && (
          <p role="alert" className="text-sm text-erro">
            {fotos.erro}
          </p>
        )}
        {cabeMaisFoto ? (
          <BotaoDeFoto
            rotulo={temFotoGeral ? 'Tirar outra foto' : 'Tirar foto dos equipamentos'}
            destaque={!temFotoGeral}
            aoEscolher={(arquivo) => void fotos.adicionar(arquivo, 'equipamento', null)}
          />
        ) : (
          <p className="text-sm text-texto-suave">Limite de {LIMITE_DE_FOTOS} fotos atingido.</p>
        )}
      </Card>

      <p className="flex items-center gap-2 text-[13px] text-texto-suave">
        <MapPin aria-hidden className="size-4 shrink-0" />
        GPS e hora entram sozinhos ao confirmar.
      </p>

      {mensagemDeFalha && (
        <p role="alert" className="rounded-[10px] bg-erro-fundo px-3.5 py-2.5 text-sm text-erro">
          {mensagemDeFalha}
        </p>
      )}

      <div className="sticky bottom-16 flex flex-col gap-2 bg-gradient-to-t from-fundo from-70% pt-4 pb-3">
        <Button
          type="submit"
          tamanho="grande"
          disabled={faltando > 0 || !temFotoGeral || semEtiqueta.length > 0 || fotos.enviando || registrar.isPending}
        >
          {registrar.isPending
            ? 'Registrando'
            : faltando > 0
              ? `Confirmar retirada · ${faltando === 1 ? 'falta 1 item' : `faltam ${faltando} itens`}`
              : fotos.enviando
                ? 'Enviando fotos'
                : !temFotoGeral
                  ? 'Confirmar retirada · falta a foto'
                  : semEtiqueta.length > 0
                    ? 'Confirmar retirada · falta foto da etiqueta'
                    : 'Confirmar retirada'}
        </Button>
        <Button asChild variante="fantasma" tamanho="compacto">
          <Link to={`/tecnico/ordens/${ordem.id}/visita`}>Visita sem sucesso</Link>
        </Button>
      </div>
    </form>
  )
}

/** Série digitada contra a cadastrada. Fonte não tem série. */
function situacaoDaSerie(e: Equipamento, serie: string): 'sem_serie' | 'vazia' | 'confere' | 'diferente' {
  if (e.numeroSerie === null) return 'sem_serie'
  const lida = normalizarSerie(serie)
  if (lida.length === 0) return 'vazia'
  return lida === normalizarSerie(e.numeroSerie) ? 'confere' : 'diferente'
}

function ItemDaRetirada({
  posicao,
  equipamento: e,
  serie,
  recolhido,
  aoMudarSerie,
  aoMarcar,
  children,
}: {
  posicao: number
  equipamento: Equipamento
  serie: string
  recolhido: boolean
  aoMudarSerie: (v: string) => void
  aoMarcar: (v: boolean) => void
  children?: ReactNode
}) {
  const idCampo = useId()
  const lida = normalizarSerie(serie)
  const confere = situacaoDaSerie(e, serie) === 'confere'

  return (
    <Card className="gap-3">
      <p className="font-semibold">
        {posicao} · {rotuloDoTipo[e.tipo]} · {e.modelo}
      </p>
      {e.numeroSerie !== null ? (
        <div className="flex flex-col gap-1.5">
          <label htmlFor={idCampo} className="text-sm font-medium">
            SN da etiqueta <span className="font-normal text-texto-suave">(esperado: {e.numeroSerie})</span>
          </label>
          <p id={`${idCampo}-dica`} className="text-[13px] text-texto-suave">
            O código depois de &quot;SN&quot;. Não use o MAC.
          </p>
          <input
            id={idCampo}
            value={serie}
            onChange={(ev) => aoMudarSerie(ev.target.value)}
            autoCapitalize="characters"
            autoComplete="off"
            spellCheck={false}
            className="min-h-12 w-full rounded-[10px] border border-borda-forte bg-superficie px-3 font-titulo text-base tracking-wide uppercase"
            aria-describedby={lida ? `${idCampo}-dica ${idCampo}-situacao` : `${idCampo}-dica`}
          />
          {lida.length > 0 && (
            <p id={`${idCampo}-situacao`} className={confere ? 'text-sm font-medium text-sucesso' : 'flex gap-1.5 text-sm font-medium text-alerta'}>
              {confere ? (
                'SN confere'
              ) : (
                <>
                  <TriangleAlert aria-hidden className="mt-0.5 size-4 shrink-0" />
                  SN diferente do cadastrado: vai registrado como divergência para o estoque.
                </>
              )}
            </p>
          )}
        </div>
      ) : (
        <label className="flex min-h-11 items-center gap-3 text-[15px]">
          <input type="checkbox" checked={recolhido} onChange={(ev) => aoMarcar(ev.target.checked)} className="size-6 accent-azul" />
          Recolhida
        </label>
      )}
      {children}
    </Card>
  )
}

export function Bloqueio({ texto, voltarPara }: { texto: string; voltarPara: string }) {
  return (
    <div role="alert" className="flex flex-col items-start gap-3 rounded-xl bg-erro-fundo p-4 text-erro">
      <p className="flex items-start gap-2 font-medium">
        <TriangleAlert aria-hidden className="mt-0.5 size-5 shrink-0" />
        {texto}
      </p>
      <Button asChild variante="neutro" tamanho="compacto">
        <Link to={voltarPara}>Voltar para as ordens</Link>
      </Button>
    </div>
  )
}
