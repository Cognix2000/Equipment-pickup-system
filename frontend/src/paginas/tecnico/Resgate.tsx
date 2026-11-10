import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, ChevronLeft, Copy, ExternalLink, KeyRound, Wallet } from 'lucide-react'
import { useId, useState } from 'react'
import { Link } from 'react-router'
import { Carregando, FalhaAoCarregar } from '@/components/EstadoDaTela'
import { Badge, type TomDoSelo } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardTitle } from '@/components/ui/card'
import { classeDoInput } from '@/components/ui/input'
import { emReais } from '@/features/repasses/repasses'
import {
  cadastrarChavePix,
  meuResgateQuery,
  rotuloDoTipoDeChave,
  solicitarResgate,
  textoDaSituacao,
  tipoDeChave,
  type MeuResgate,
  type ResgateFeito,
  type SituacaoDoResgate,
  type TipoDeChave,
} from '@/features/repasses/resgate'
import { ErroDaApi } from '@/lib/api'
import { AvisoDeErro, Campo } from '@/paginas/acesso/LayoutDeAcesso'

const formatoDataHora = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short', timeZone: 'America/Sao_Paulo' })
const tomDaSituacao: Record<SituacaoDoResgate, TomDoSelo> = { solicitado: 'info', enviado: 'info', pago: 'sucesso', falhou: 'erro' }
const mensagemDe = (erro: unknown) => (erro instanceof ErroDaApi ? erro.message : 'Sem resposta do servidor. Tente de novo.')

/**
 * Resgate do técnico terceiro: o saldo das retiradas que o estoque já conferiu vai por PIX para a chave aprovada,
 * como o saque de um entregador de aplicativo. O CLT recebe na folha e só vê o aviso.
 */
export function Resgate() {
  const { data, isPending, isError, error, refetch } = useQuery(meuResgateQuery)

  return (
    <>
      <header className="flex items-center gap-2 bg-marinho px-3 pt-3 pb-4 text-white">
        <Link to="/tecnico/ordens" aria-label="Voltar para as ordens" className="grid size-12 place-items-center text-white">
          <ChevronLeft aria-hidden className="size-6" />
        </Link>
        <h1 className="text-xl font-bold">Resgatar</h1>
      </header>
      <main className="flex flex-1 flex-col gap-3 p-4">
        {isPending && <Carregando linhas={3} rotulo="Carregando o resgate" />}
        {isError && <FalhaAoCarregar erro={error} tentarDeNovo={() => void refetch()} />}
        {data?.vinculo === 'clt' && (
          <Card>
            <CardTitle className="text-base sm:text-base">Seu repasse vai na folha</CardTitle>
            <p className="text-[15px] text-texto-suave">
              Você é CLT: o valor das retiradas conferidas entra no salário, no fim do mês. Acompanhe quanto já tem na tela das ordens.
            </p>
          </Card>
        )}
        {data?.vinculo === 'terceiro' && <ResgateDoTerceiro dados={data} />}
      </main>
    </>
  )
}

function ResgateDoTerceiro({ dados }: { dados: MeuResgate }) {
  const aprovada = dados.chaves.find((c) => c.situacao === 'aprovada')
  return (
    <>
      <SaldoParaResgatar dados={dados} chaveMascarada={aprovada?.mascarada} />
      <ChavePix dados={dados} />
      {dados.resgates.length > 0 && (
        <section aria-labelledby="titulo-dos-resgates" className="flex flex-col gap-2">
          <h2 id="titulo-dos-resgates" className="text-base font-semibold text-marinho">
            Seus resgates
          </h2>
          <ul className="flex flex-col gap-2">
            {dados.resgates.map((r) => (
              <ResgateNaLista key={r.id} resgate={r} />
            ))}
          </ul>
          <p className="text-[13px] text-texto-suave">
            Se um PIX pago não aparecer na sua conta, mostre ao seu banco o ID de Fim a Fim: é o código do Banco Central que prova a transferência.
          </p>
        </section>
      )}
    </>
  )
}

function SaldoParaResgatar({ dados, chaveMascarada }: { dados: MeuResgate; chaveMascarada?: string }) {
  const clienteDeConsultas = useQueryClient()
  const [confirmando, setConfirmando] = useState(false)
  const resgatar = useMutation({
    mutationFn: solicitarResgate,
    onSuccess: (atual) => clienteDeConsultas.setQueryData(meuResgateQuery.queryKey, atual),
    onSettled: () => {
      setConfirmando(false)
      return clienteDeConsultas.invalidateQueries({ queryKey: ['repasses'] })
    },
  })
  const { disponivel, minimoCentavos } = dados
  const aCaminho = dados.resgates.some((r) => r.situacao === 'solicitado' || r.situacao === 'enviado')
  const impedimento = !dados.resgateLigado
    ? 'O resgate por PIX ainda não está ligado. Fale com o financeiro.'
    : !chaveMascarada
      ? 'Cadastre a chave PIX abaixo. O resgate libera depois que o financeiro aprovar.'
      : aCaminho
        ? 'Tem um PIX a caminho. Espere ele cair para pedir outro.'
        : disponivel.valorCentavos < minimoCentavos
          ? `O resgate mínimo é de ${emReais(minimoCentavos)}.`
          : null

  return (
    <Card>
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="flex items-center gap-1.5 text-[13px] text-texto-suave">
            <Wallet aria-hidden className="size-4" />
            Pode resgatar agora
          </p>
          <p className="font-titulo text-[32px] leading-tight font-bold text-marinho tabular-nums">{emReais(disponivel.valorCentavos)}</p>
          <p className="text-[13px] text-texto-suave">
            {disponivel.quantidade === 1 ? '1 retirada conferida pelo estoque' : `${disponivel.quantidade} retiradas conferidas pelo estoque`}
          </p>
        </div>
      </div>
      <AvisoDeErro mensagem={resgatar.error ? mensagemDe(resgatar.error) : undefined} />
      {impedimento ? (
        <p className="rounded-[10px] bg-fundo px-3.5 py-2.5 text-sm text-texto-suave">{impedimento}</p>
      ) : confirmando ? (
        <div className="flex flex-col gap-2">
          <p className="text-[15px] font-medium">
            Mandar {emReais(disponivel.valorCentavos)} por PIX para a chave {chaveMascarada}?
          </p>
          <div className="flex flex-wrap gap-2">
            <Button disabled={resgatar.isPending} onClick={() => resgatar.mutate()}>
              {resgatar.isPending ? 'Enviando' : 'Sim, resgatar'}
            </Button>
            <Button variante="fantasma" onClick={() => setConfirmando(false)}>
              Voltar
            </Button>
          </div>
        </div>
      ) : (
        <Button tamanho="grande" onClick={() => setConfirmando(true)}>
          Resgatar {emReais(disponivel.valorCentavos)}
        </Button>
      )}
    </Card>
  )
}

function ChavePix({ dados }: { dados: MeuResgate }) {
  const aprovada = dados.chaves.find((c) => c.situacao === 'aprovada')
  const pendente = dados.chaves.find((c) => c.situacao === 'pendente')
  const recusada = dados.chaves.find((c) => c.situacao === 'recusada')
  const [trocando, setTrocando] = useState(false)
  const mostrarFormulario = !aprovada && !pendente ? true : trocando

  return (
    <Card>
      <CardTitle className="flex items-center gap-2 text-base sm:text-base">
        <KeyRound aria-hidden className="size-5" /> Chave PIX
      </CardTitle>
      {aprovada && (
        <p className="text-[15px]">
          {rotuloDoTipoDeChave[aprovada.tipo]} <span className="font-semibold tabular-nums">{aprovada.mascarada}</span>
        </p>
      )}
      {pendente && (
        <p role="status" className="rounded-[10px] bg-alerta-fundo px-3.5 py-2.5 text-sm font-medium text-alerta">
          {rotuloDoTipoDeChave[pendente.tipo]} {pendente.mascarada}: esperando o financeiro aprovar.
          {aprovada ? ' Até lá, o resgate vai para a chave atual.' : ''}
        </p>
      )}
      {recusada && (
        <p className="rounded-[10px] bg-erro-fundo px-3.5 py-2.5 text-sm text-erro">
          A chave {recusada.mascarada} foi recusada{recusada.motivoRecusa ? `: ${recusada.motivoRecusa}` : '.'} Cadastre outra.
        </p>
      )}
      {mostrarFormulario ? (
        <FormularioDaChave aoTerminar={() => setTrocando(false)} podeCancelar={Boolean(aprovada || pendente)} />
      ) : (
        <Button variante="contorno" tamanho="compacto" className="self-start" onClick={() => setTrocando(true)}>
          Trocar a chave
        </Button>
      )}
    </Card>
  )
}

function FormularioDaChave({ aoTerminar, podeCancelar }: { aoTerminar: () => void; podeCancelar: boolean }) {
  const clienteDeConsultas = useQueryClient()
  const [tipo, setTipo] = useState<TipoDeChave>('cpf')
  const [chave, setChave] = useState('')
  const idDoTipo = useId()
  const idDaChave = useId()
  const enviarChave = useMutation({
    mutationFn: () => cadastrarChavePix(tipo, chave),
    onSuccess: async () => {
      setChave('')
      aoTerminar()
      await clienteDeConsultas.invalidateQueries({ queryKey: meuResgateQuery.queryKey })
    },
  })

  return (
    <form
      className="flex flex-col gap-3"
      noValidate
      onSubmit={(e) => {
        e.preventDefault()
        enviarChave.mutate()
      }}
    >
      <AvisoDeErro mensagem={enviarChave.error ? mensagemDe(enviarChave.error) : undefined} />
      <Campo id={idDoTipo} rotulo="Tipo da chave">
        <select id={idDoTipo} className={classeDoInput} value={tipo} onChange={(e) => setTipo(tipoDeChave.parse(e.target.value))}>
          {tipoDeChave.options.map((t) => (
            <option key={t} value={t}>
              {rotuloDoTipoDeChave[t]}
            </option>
          ))}
        </select>
      </Campo>
      <Campo id={idDaChave} rotulo="Chave" ajuda="Use uma chave no seu nome (ou no CNPJ da sua empresa). O financeiro confere antes de aprovar.">
        <input
          id={idDaChave}
          className={classeDoInput}
          aria-describedby={`${idDaChave}-ajuda`}
          inputMode={tipo === 'cpf' || tipo === 'cnpj' || tipo === 'telefone' ? 'numeric' : tipo === 'email' ? 'email' : 'text'}
          autoComplete="off"
          value={chave}
          onChange={(e) => setChave(e.target.value)}
        />
      </Campo>
      <div className="flex flex-wrap gap-2">
        <Button type="submit" disabled={enviarChave.isPending || chave.trim().length === 0}>
          {enviarChave.isPending ? 'Enviando' : 'Enviar para aprovação'}
        </Button>
        {podeCancelar && (
          <Button type="button" variante="fantasma" onClick={aoTerminar}>
            Cancelar
          </Button>
        )}
      </div>
    </form>
  )
}

function ResgateNaLista({ resgate: r }: { resgate: ResgateFeito }) {
  const [copiado, setCopiado] = useState(false)
  return (
    <li className="flex flex-col gap-1.5 rounded-xl bg-superficie p-3.5">
      <div className="flex items-center justify-between gap-3">
        <span className="font-titulo text-lg font-bold text-marinho tabular-nums">{emReais(r.valorCentavos)}</span>
        <Badge tom={tomDaSituacao[r.situacao]}>{textoDaSituacao[r.situacao]}</Badge>
      </div>
      <p className="text-[13px] text-texto-suave">
        Pedido em {formatoDataHora.format(new Date(r.solicitadoEm))}
        {r.concluidoEm && r.situacao === 'pago' ? ` · caiu em ${formatoDataHora.format(new Date(r.concluidoEm))}` : ''}
      </p>
      {(r.situacao === 'solicitado' || r.situacao === 'enviado') && <p className="text-[13px] text-texto-suave">Confirmando com o banco…</p>}
      {r.situacao === 'falhou' && <p className="text-[13px] text-erro">{r.motivoFalha ?? 'O PIX não saiu.'} O valor voltou para o seu saldo.</p>}
      {r.situacao === 'pago' && r.idFimAFim && (
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-[13px] break-all">
            ID de Fim a Fim: <span className="font-medium tabular-nums">{r.idFimAFim}</span>
          </span>
          <Button
            variante="fantasma"
            tamanho="compacto"
            onClick={async () => {
              await navigator.clipboard.writeText(r.idFimAFim!)
              setCopiado(true)
            }}
          >
            {copiado ? <Check aria-hidden /> : <Copy aria-hidden />}
            {copiado ? 'Copiado' : 'Copiar'}
          </Button>
        </div>
      )}
      {r.comprovanteUrl && (
        <a
          href={r.comprovanteUrl}
          target="_blank"
          rel="noreferrer"
          className="inline-flex min-h-11 items-center gap-1.5 self-start text-sm font-semibold text-azul"
        >
          <ExternalLink aria-hidden className="size-4" />
          Ver comprovante
        </a>
      )}
    </li>
  )
}
