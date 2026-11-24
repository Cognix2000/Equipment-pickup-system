import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ChevronLeft, Pencil, Plus, Trash2 } from 'lucide-react'
import { useId, useRef, useState, type ReactNode } from 'react'
import { Link, useParams } from 'react-router'
import { Carregando, FalhaAoCarregar } from '@/components/EstadoDaTela'
import { Badge, type TomDoSelo } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardTitle } from '@/components/ui/card'
import { classeDoInput } from '@/components/ui/input'
import { useEu, usuariosQuery, type Papel } from '@/features/acesso/acesso'
import { comandos, editarOrdem, novaChave, type EquipamentoAEditar, type MotivoDeCancelamento, type PedidoDeEdicao } from '@/features/ordens/comandos'
import { formatarTelefone, janelaDeContato } from '@/features/ordens/contato'
import { enderecoDaFoto } from '@/features/ordens/fotos'
import { useOrdem } from '@/features/ordens/consultas'
import { Historico } from '@/features/ordens/Historico'
import { descreverPrazo, statusComEquipamentoNoCliente } from '@/features/ordens/prazo'
import { RegistroDeContato } from '@/features/ordens/RegistroDeContato'
import { StatusBadge } from '@/features/ordens/StatusBadge'
import { caixaDeTitulo, enderecoCurto, rotuloDoTipo, type Equipamento, type Ordem, type TipoEquipamento, type Telefone } from '@/features/ordens/tipos'
import { mensagemDaFalha, useComandoDaOrdem } from '@/features/ordens/useComandoDaOrdem'

const formatoData = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short', timeZone: 'America/Sao_Paulo' })

const encerrados: Ordem['status'][] = ['conferida', 'cancelada', 'devolvida_na_loja', 'encerrada_sem_retirada']
const podeEditar = (o: Ordem) => !encerrados.includes(o.status)
const ehGestorOuFinanceiro = (papel?: Papel) => papel === 'financeiro' || papel === 'admin'

const tiposDeEquipamento: { valor: TipoEquipamento; texto: string }[] = [
  { valor: 'ont', texto: 'ONT' },
  { valor: 'onu', texto: 'ONU' },
  { valor: 'roteador', texto: 'Roteador' },
  { valor: 'repetidor_mesh', texto: 'Repetidor Mesh' },
  { valor: 'fonte', texto: 'Fonte' },
]

const situacaoDoItem: Record<Equipamento['situacao'], { texto: string; tom: TomDoSelo }> = {
  pendente: { texto: 'Com o cliente', tom: 'neutro' },
  retirado: { texto: 'Retirado', tom: 'info' },
  devolvido_na_loja: { texto: 'Devolvido na loja', tom: 'sucesso' },
  conferido: { texto: 'Conferido', tom: 'sucesso' },
  divergente: { texto: 'Divergente', tom: 'alerta' },
  extraviado: { texto: 'Extraviado', tom: 'erro' },
}

/** Contato só depois da carência e antes da retirada (RN10). */
const aceitaContato = (o: Ordem) => o.status === 'disponivel' || o.status === 'atribuida' || o.status === 'em_rota'
const podeCancelar = (o: Ordem) => ['nova', 'disponivel', 'atribuida', 'em_rota', 'nao_concluida'].includes(o.status)
const ehDoFinanceiro = (papel?: Papel) => papel === 'financeiro' || papel === 'admin'

/** RF07 no painel: tudo da ordem e o que o financeiro (ou o gestor) pode fazer nela agora. */
export function DetalheDaOrdem() {
  const { id = '' } = useParams()
  const { data: ordem, isPending, isError, error, refetch } = useOrdem(id)
  const { data: eu } = useEu()

  return (
    <>
      <Link to="/financeiro/ordens" className="inline-flex min-h-11 items-center gap-1 self-start font-semibold text-azul no-underline">
        <ChevronLeft aria-hidden className="size-5" />
        Ordens
      </Link>
      {isPending && <Carregando linhas={4} rotulo="Carregando a ordem" />}
      {isError && <FalhaAoCarregar erro={error} tentarDeNovo={() => void refetch()} />}
      {ordem && <ConteudoDaOrdem ordem={ordem} papel={eu?.papel} />}
    </>
  )
}

function ConteudoDaOrdem({ ordem, papel }: { ordem: Ordem; papel?: Papel }) {
  const semTentativa = aceitaContato(ordem) && !(ordem.historico ?? []).some((e) => e.tipo === 'contato')

  return (
    <>
      <div className="flex flex-col gap-2">
        <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5">
          <h1 className="text-[28px] font-bold text-marinho">OS {ordem.numeroOs ?? 's/ nº'}</h1>
          <StatusBadge status={ordem.status} />
          {ordem.aguardaBaixaNoHubsoft && <Badge tom="alerta">Dar baixa</Badge>}
        </div>
        <p className="text-[15px] text-texto-suave">
          {ordem.tipoOs} · <Prazo ordem={ordem} />
        </p>
      </div>

      <div className="grid items-start gap-5 lg:grid-cols-[minmax(0,1fr)_380px]">
        <div className="flex min-w-0 flex-col gap-5">
          <Card>
            <CardTitle className="text-base sm:text-lg">{ordem.cliente}</CardTitle>
            <div>
              <p>
                {enderecoCurto(ordem)}
                {ordem.endereco.complemento && ` · ${caixaDeTitulo(ordem.endereco.complemento)}`}
              </p>
              <p className="text-[13px] text-texto-suave">
                {caixaDeTitulo(ordem.endereco.bairro)} · {caixaDeTitulo(ordem.endereco.cidade)} - {ordem.endereco.uf}
                {ordem.endereco.referencia && ` · ${caixaDeTitulo(ordem.endereco.referencia)}`}
              </p>
            </div>
            {ordem.plano && (
              <p className="rounded-lg bg-mapa/40 px-2.5 py-2 text-[13px] font-medium text-marinho">
                <span className="text-texto-suave">Plano:</span> {ordem.plano}
              </p>
            )}
            {ordem.observacao && <p className="rounded-lg bg-fundo px-2.5 py-2 text-[13px] text-texto-suave">Obs.: {ordem.observacao}</p>}
          </Card>

          <Card>
            <CardTitle className="text-base sm:text-lg">Contato com o cliente</CardTitle>
            {semTentativa && (
              <p className="rounded-[10px] bg-alerta-fundo px-3.5 py-2.5 text-sm font-medium text-alerta">
                Nenhuma tentativa de contato registrada. Sem tentativa, a ordem não encerra sozinha depois do prazo.
              </p>
            )}
            <Telefones ordem={ordem} podeRegistrar={ehDoFinanceiro(papel) && aceitaContato(ordem)} />
          </Card>

          <Card>
            <CardTitle className="text-base sm:text-lg">
              {ordem.equipamentos.length} {ordem.equipamentos.length === 1 ? 'item' : 'itens'}
            </CardTitle>
            <ul className="flex flex-col divide-y divide-borda">
              {ordem.equipamentos.map((e) => (
                <li key={e.id} className="flex flex-wrap items-center justify-between gap-2 py-2 first:pt-0 last:pb-0">
                  <div>
                    <p className="font-semibold">
                      {rotuloDoTipo[e.tipo]} · {e.modelo}
                    </p>
                    <p className="text-[13px] text-texto-suave">{e.numeroSerie ? `SN ${e.numeroSerie}` : 'Sem SN'}</p>
                  </div>
                  <Badge tom={situacaoDoItem[e.situacao].tom}>{situacaoDoItem[e.situacao].texto}</Badge>
                </li>
              ))}
            </ul>
          </Card>

          <FotosDaOrdem ordem={ordem} />

          <Historico eventos={ordem.historico} />
        </div>

        <Acoes ordem={ordem} papel={papel} />
      </div>
    </>
  )
}

/** RN14: as fotos da retirada, servidas pela API (que confere a permissão). Clique abre a foto inteira. */
function FotosDaOrdem({ ordem }: { ordem: Ordem }) {
  const fotos = ordem.fotos ?? []
  if (fotos.length === 0) return null
  const modelo = (equipamentoId: string | null) => ordem.equipamentos.find((e) => e.id === equipamentoId)?.modelo
  return (
    <Card>
      <CardTitle className="text-base sm:text-lg">Fotos da retirada</CardTitle>
      <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3">
        {fotos.map((f) => {
          const descricao = f.tipo === 'etiqueta_serie' ? `Etiqueta · ${modelo(f.equipamentoId) ?? 'item'}` : 'Equipamentos'
          return (
            <li key={f.id}>
              <a href={enderecoDaFoto(ordem.id, f.id)} target="_blank" rel="noreferrer" className="flex flex-col gap-1 no-underline">
                <img
                  src={enderecoDaFoto(ordem.id, f.id)}
                  alt={`${descricao}, ${formatoData.format(new Date(f.capturadaEm))}`}
                  loading="lazy"
                  className="aspect-[4/3] w-full rounded-lg bg-fundo object-cover"
                />
                <span className="text-[13px] font-medium text-texto">{descricao}</span>
                <span className="text-[13px] text-texto-suave tabular-nums">{formatoData.format(new Date(f.capturadaEm))}</span>
              </a>
            </li>
          )
        })}
      </ul>
    </Card>
  )
}

function Prazo({ ordem }: { ordem: Ordem }) {
  if (ordem.status === 'nova') return <>Em carência até {formatoData.format(new Date(ordem.carenciaAte))}</>
  if (!statusComEquipamentoNoCliente.has(ordem.status)) return <>Equipamento recolhido ou ordem encerrada</>
  return <>{descreverPrazo(ordem.carenciaAte).texto}</>
}

const linha: Record<Telefone['linha'], string | null> = { nao_verificada: null, atende: 'Linha atende', inexistente: 'Número não existe' }
const whatsapp: Record<Telefone['whatsapp'], string | null> = {
  nao_verificado: null,
  tem: 'Tem WhatsApp',
  nao_tem: 'Sem WhatsApp',
  nao_entregue: 'WhatsApp não entregue',
}

/**
 * RN10 no painel: o financeiro registra a ligação ou a mensagem que fez. O sistema não liga nem manda
 * mensagem. Fora da janela de contato, o registro é aceito e fica marcado para o gestor.
 */
function Telefones({ ordem, podeRegistrar }: { ordem: Ordem; podeRegistrar: boolean }) {
  const [aberto, setAberto] = useState<{ telefone: string; canal: 'ligacao' | 'whatsapp' } | null>(null)
  const [registrado, setRegistrado] = useState(false)
  const telefones = ordem.telefones ?? []
  if (telefones.length === 0) return <p className="text-sm text-texto-suave">Sem telefone cadastrado: o contato fica só pela visita.</p>
  const janela = janelaDeContato()

  const abrir = (telefone: string, canal: 'ligacao' | 'whatsapp') => {
    setRegistrado(false)
    setAberto(aberto?.telefone === telefone && aberto.canal === canal ? null : { telefone, canal })
  }

  return (
    <div data-teste="telefones" className="flex flex-col gap-3">
      <ul className="flex flex-col divide-y divide-borda">
        {telefones.map((t) => {
          const valido = t.formatoValido && t.linha !== 'inexistente'
          const selos = [linha[t.linha], whatsapp[t.whatsapp]].filter((s): s is string => s !== null)
          return (
            <li key={t.id} className="flex flex-col gap-2 py-2.5 first:pt-0 last:pb-0">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div className="flex flex-wrap items-center gap-2">
                  <span className={valido ? 'font-medium tabular-nums' : 'text-texto-suave tabular-nums'}>
                    {formatarTelefone(t.numero)}
                    {!t.formatoValido && ' · fora do padrão'}
                  </span>
                  {selos.map((s) => (
                    <Badge key={s} tom={s === 'Número não existe' || s === 'Sem WhatsApp' ? 'erro' : 'neutro'}>
                      {s}
                    </Badge>
                  ))}
                </div>
                {podeRegistrar && valido && (
                  <div className="flex flex-wrap gap-2">
                    <Button
                      type="button"
                      variante="contorno"
                      tamanho="compacto"
                      aria-expanded={aberto?.telefone === t.id && aberto.canal === 'ligacao'}
                      onClick={() => abrir(t.id, 'ligacao')}
                    >
                      Registrar ligação
                    </Button>
                    <Button
                      type="button"
                      variante="contorno"
                      tamanho="compacto"
                      aria-expanded={aberto?.telefone === t.id && aberto.canal === 'whatsapp'}
                      onClick={() => abrir(t.id, 'whatsapp')}
                    >
                      Registrar WhatsApp
                    </Button>
                  </div>
                )}
              </div>
              {aberto?.telefone === t.id && (
                <RegistroDeContato
                  key={aberto.canal}
                  ordemId={ordem.id}
                  telefone={t}
                  canal={aberto.canal}
                  aoRegistrar={() => {
                    setAberto(null)
                    setRegistrado(true)
                  }}
                />
              )}
            </li>
          )
        })}
      </ul>
      {registrado && (
        <p role="status" className="rounded-[10px] bg-sucesso-fundo px-3.5 py-2.5 text-sm font-medium text-sucesso">
          Contato registrado no histórico.
        </p>
      )}
      {podeRegistrar && (
        <p className={janela.permitido ? 'text-[13px] text-texto-suave' : 'text-[13px] font-medium text-alerta'}>
          {janela.permitido
            ? 'Contato de segunda a sábado, das 8h às 18h.'
            : 'Fora da janela de contato (segunda a sábado, das 8h às 18h): o registro fica marcado para o gestor.'}
        </p>
      )}
    </div>
  )
}

/** O que dá para fazer na ordem agora, conforme a situação e o papel de quem está vendo. */
function Acoes({ ordem, papel }: { ordem: Ordem; papel?: Papel }) {
  const [feito, setFeito] = useState<string | null>(null)
  const financeiro = ehDoFinanceiro(papel)
  const gestorOuFinanceiro = ehGestorOuFinanceiro(papel)
  const acoes = [
    gestorOuFinanceiro && podeEditar(ordem) && <EditarOrdemAcao key="editar" ordem={ordem} aoConcluir={setFeito} />,
    financeiro && ordem.status === 'nova' && <Liberar key="liberar" ordem={ordem} aoConcluir={setFeito} />,
    financeiro && ordem.status === 'nova' && <AjustarCarencia key="carencia" ordem={ordem} aoConcluir={setFeito} />,
    papel === 'admin' && ordem.status === 'disponivel' && <Designar key="designar" ordem={ordem} aoConcluir={setFeito} />,
    financeiro && ordem.aguardaBaixaNoHubsoft && <MarcarBaixa key="baixa" ordem={ordem} aoConcluir={setFeito} />,
    financeiro && podeCancelar(ordem) && <Cancelar key="cancelar" ordem={ordem} aoConcluir={setFeito} />,
  ].filter(Boolean)

  return (
    // No celular, o que fazer vem logo depois do cabeçalho; no computador, fica fixo na coluna da direita.
    <Card className="order-first lg:sticky lg:top-6 lg:order-none">
      <CardTitle className="text-base sm:text-lg">O que fazer</CardTitle>
      {feito && (
        <p role="status" className="rounded-[10px] bg-sucesso-fundo px-3.5 py-2.5 text-sm font-medium text-sucesso">
          {feito}
        </p>
      )}
      {acoes.length > 0 ? (
        <div className="flex flex-col divide-y divide-borda">{acoes}</div>
      ) : (
        <p className="text-sm text-texto-suave">Nada a fazer nesta ordem agora.</p>
      )}
    </Card>
  )
}

type PropsDaAcao = { ordem: Ordem; aoConcluir: (aviso: string) => void }

function Acao({ titulo, children }: { titulo: string; children: ReactNode }) {
  return (
    <div className="flex flex-col gap-2 py-3 first:pt-0 last:pb-0">
      <h3 className="font-semibold text-marinho">{titulo}</h3>
      {children}
    </div>
  )
}

function Falha({ erro }: { erro: Error | null }) {
  const mensagem = mensagemDaFalha(erro)
  return mensagem ? (
    <p role="alert" className="text-sm text-erro">
      {mensagem}
    </p>
  ) : null
}

/** Edição de todos os campos da ordem pelo financeiro ou gestor (qualquer status antes do encerramento). */
function EditarOrdemAcao({ ordem, aoConcluir }: PropsDaAcao) {
  const [aberto, setAberto] = useState(false)
  const chaveRef = useRef(novaChave())
  const queryClient = useQueryClient()

  const editar = useMutation({
    mutationFn: (pedido: PedidoDeEdicao) => editarOrdem(ordem.id, pedido, chaveRef.current),
    onSuccess: (ordemAtualizada) => {
      queryClient.setQueryData(['ordens', ordem.id], ordemAtualizada)
      void queryClient.invalidateQueries({ queryKey: ['ordens'] })
      setAberto(false)
      chaveRef.current = novaChave()
      aoConcluir('Ordem atualizada com sucesso.')
    },
  })

  if (!aberto)
    return (
      <Acao titulo="Editar a ordem">
        <p className="text-sm text-texto-suave">Corrija ou complete qualquer campo da ordem antes do encerramento.</p>
        <Button type="button" variante="contorno" tamanho="compacto" className="inline-flex gap-1.5 self-start" onClick={() => setAberto(true)}>
          <Pencil className="size-4" aria-hidden />
          Editar
        </Button>
      </Acao>
    )

  return (
    <Acao titulo="Editar a ordem">
      <FormularioDeEdicao
        ordem={ordem}
        aoSalvar={(pedido) => editar.mutate(pedido)}
        aoCancelar={() => setAberto(false)}
        salvando={editar.isPending}
        erro={editar.error instanceof Error ? editar.error : null}
      />
    </Acao>
  )
}

type PropsDoFormulario = {
  ordem: Ordem
  aoSalvar: (pedido: PedidoDeEdicao) => void
  aoCancelar: () => void
  salvando: boolean
  erro: Error | null
}

function FormularioDeEdicao({ ordem, aoSalvar, aoCancelar, salvando, erro }: PropsDoFormulario) {
  const id = useId()
  const [numeroOs, setNumeroOs] = useState(ordem.numeroOs !== null ? String(ordem.numeroOs) : '')
  const [tipoOs, setTipoOs] = useState(ordem.tipoOs)
  const [logradouro, setLogradouro] = useState(ordem.endereco.logradouro)
  const [numero, setNumero] = useState(ordem.endereco.numero ?? '')
  const [complemento, setComplemento] = useState(ordem.endereco.complemento ?? '')
  const [bairro, setBairro] = useState(ordem.endereco.bairro)
  const [cidade, setCidade] = useState(ordem.endereco.cidade)
  const [uf, setUf] = useState(ordem.endereco.uf)
  const [referencia, setReferencia] = useState(ordem.endereco.referencia ?? '')
  const [cep, setCep] = useState('')
  const [observacao, setObservacao] = useState(ordem.observacao ?? '')
  const [plano, setPlano] = useState(ordem.plano ?? '')
  const [protocolo, setProtocolo] = useState('')
  const [equipamentos, setEquipamentos] = useState<EquipamentoAEditar[]>(() =>
    ordem.equipamentos.map((e) => ({
      id: e.id,
      tipo: e.tipo,
      modelo: e.modelo,
      serie: e.numeroSerie,
      mac: null,
      valor: null,
    }))
  )

  function atualizarEquipamento(idx: number, campo: Partial<EquipamentoAEditar>) {
    setEquipamentos((prev) => prev.map((e, i) => (i === idx ? { ...e, ...campo } : e)))
  }

  function adicionarEquipamento() {
    setEquipamentos((prev) => [...prev, { id: '', tipo: 'ont', modelo: '', serie: null, mac: null, valor: null }])
  }

  function removerEquipamento(idx: number) {
    setEquipamentos((prev) => prev.filter((_, i) => i !== idx))
  }

  function submeter(e: React.FormEvent) {
    e.preventDefault()
    const pedido: PedidoDeEdicao = {
      numeroOs: numeroOs.trim() ? Number(numeroOs) : null,
      tipoOs: tipoOs.trim(),
      endereco: {
        logradouro: logradouro.trim(),
        numero: numero.trim() || null,
        complemento: complemento.trim() || null,
        bairro: bairro.trim(),
        cidade: cidade.trim(),
        uf: uf.trim().toUpperCase(),
        referencia: referencia.trim() || null,
      },
      cep: cep.trim() || null,
      localizacao: ordem.localizacao,
      observacao: observacao.trim() || null,
      plano: plano.trim() || null,
      protocolo: protocolo.trim() || null,
      idOrdemServicoHubsoft: null,
      idClienteServicoHubsoft: null,
      dataProgramada: null,
      dataRescisao: null,
      equipamentos,
    }
    aoSalvar(pedido)
  }

  const idf = (sufixo: string) => `${id}-${sufixo}`
  const campoTexto = (label: string, valor: string, onChange: (v: string) => void, props?: React.InputHTMLAttributes<HTMLInputElement>) => (
    <label className="flex flex-col gap-0.5 text-sm">
      {label}
      <input className={classeDoInput} value={valor} onChange={(e) => onChange(e.target.value)} {...props} />
    </label>
  )

  return (
    <form onSubmit={submeter} className="flex flex-col gap-4">
      {/* OS e tipo */}
      <fieldset className="flex flex-col gap-2">
        <legend className="mb-1 text-sm font-semibold text-marinho">Identificação</legend>
        <div className="grid grid-cols-2 gap-2">
          {campoTexto('Nº da OS', numeroOs, setNumeroOs, { type: 'number', min: 1, placeholder: 'Sem nº' })}
          {campoTexto('Tipo de OS', tipoOs, setTipoOs, { required: true, placeholder: 'Ex.: REMOÇÃO' })}
        </div>
        {campoTexto('Protocolo HubSoft', protocolo, setProtocolo, { placeholder: 'Opcional' })}
        {campoTexto('Plano do cliente', plano, setPlano, { placeholder: 'Ex.: 600 Mbps' })}
      </fieldset>

      {/* Endereço */}
      <fieldset className="flex flex-col gap-2">
        <legend className="mb-1 text-sm font-semibold text-marinho">Endereço</legend>
        {campoTexto('Logradouro', logradouro, setLogradouro, { required: true })}
        <div className="grid grid-cols-2 gap-2">
          {campoTexto('Número', numero, setNumero, { placeholder: 'S/N' })}
          {campoTexto('Complemento', complemento, setComplemento)}
        </div>
        {campoTexto('Bairro', bairro, setBairro, { required: true })}
        <div className="grid grid-cols-[1fr_72px] gap-2">
          {campoTexto('Cidade', cidade, setCidade, { required: true })}
          {campoTexto('UF', uf, setUf, { required: true, maxLength: 2, placeholder: 'SP' })}
        </div>
        {campoTexto('CEP', cep, setCep, { placeholder: '00000-000' })}
        {campoTexto('Referência', referencia, setReferencia, { placeholder: 'Ponto de referência (opcional)' })}
        {campoTexto('Observação', observacao, setObservacao)}
      </fieldset>

      {/* Equipamentos */}
      <fieldset className="flex flex-col gap-2">
        <legend className="mb-1 text-sm font-semibold text-marinho">Equipamentos</legend>
        {equipamentos.map((eq, idx) => {
          const pendente = eq.id === '' || ordem.equipamentos.find((e) => e.id === eq.id)?.situacao === 'pendente'
          return (
            <div key={eq.id || idx} className="flex flex-col gap-2 rounded-lg border border-borda p-2.5">
              <div className="flex items-center justify-between gap-2">
                <label htmlFor={idf(`tipo-${idx}`)} className="text-sm font-medium">
                  Item {idx + 1}
                </label>
                {pendente && (
                  <Button
                    type="button"
                    variante="fantasma"
                    tamanho="compacto"
                    onClick={() => removerEquipamento(idx)}
                    aria-label={`Remover item ${idx + 1}`}
                  >
                    <Trash2 className="size-4 text-erro" aria-hidden />
                  </Button>
                )}
              </div>
              <div className="grid grid-cols-2 gap-2">
                <label className="flex flex-col gap-0.5 text-sm">
                  Tipo
                  <select
                    id={idf(`tipo-${idx}`)}
                    className={classeDoInput}
                    value={eq.tipo}
                    onChange={(e) => atualizarEquipamento(idx, { tipo: e.target.value as TipoEquipamento })}
                    disabled={!pendente}
                  >
                    {tiposDeEquipamento.map((t) => (
                      <option key={t.valor} value={t.valor}>{t.texto}</option>
                    ))}
                  </select>
                </label>
                <label className="flex flex-col gap-0.5 text-sm">
                  Modelo
                  <input
                    className={classeDoInput}
                    value={eq.modelo}
                    onChange={(e) => atualizarEquipamento(idx, { modelo: e.target.value })}
                    required
                    placeholder="Marca e modelo"
                  />
                </label>
              </div>
              {eq.tipo !== 'fonte' && (
                <label className="flex flex-col gap-0.5 text-sm">
                  Número de série
                  <input
                    className={classeDoInput}
                    value={eq.serie ?? ''}
                    onChange={(e) => atualizarEquipamento(idx, { serie: e.target.value || null })}
                    placeholder="Opcional até a retirada"
                    disabled={!pendente}
                  />
                </label>
              )}
              {!pendente && (
                <p className="text-xs text-texto-suave">Item já retirado ou devolvido — tipo e série não podem ser alterados.</p>
              )}
            </div>
          )
        })}
        <Button type="button" variante="contorno" tamanho="compacto" className="inline-flex gap-1.5 self-start" onClick={adicionarEquipamento}>
          <Plus className="size-4" aria-hidden />
          Adicionar item
        </Button>
      </fieldset>

      {erro && <Falha erro={erro} />}

      <div className="flex flex-wrap gap-2">
        <Button type="submit" tamanho="compacto" disabled={salvando}>
          {salvando ? 'Salvando…' : 'Salvar alterações'}
        </Button>
        <Button type="button" variante="fantasma" tamanho="compacto" disabled={salvando} onClick={aoCancelar}>
          Cancelar
        </Button>
      </div>
    </form>
  )
}

/** RN03: liberar antes do fim da carência, quando o financeiro já deu a baixa no HubSoft. */
function Liberar({ ordem, aoConcluir }: PropsDaAcao) {
  const liberar = useComandoDaOrdem(ordem.id, (_: void, chave) => comandos.liberar(ordem.id, chave), () =>
    aoConcluir('Ordem liberada: já pode ser designada a um técnico.'),
  )
  return (
    <Acao titulo="Liberar para retirada">
      <p className="text-sm text-texto-suave">
        A carência vai até {formatoData.format(new Date(ordem.carenciaAte))}. Depois disso a ordem é liberada sozinha.
      </p>
      <Button
        type="button"
        tamanho="compacto"
        className="self-start"
        disabled={liberar.isPending}
        onClick={() => liberar.mutate()}
      >
        Liberar agora
      </Button>
      <Falha erro={liberar.error} />
    </Acao>
  )
}

/** RN03 ajustável: o financeiro muda quando a ordem sai da carência, caso a caso. */
function AjustarCarencia({ ordem, aoConcluir }: PropsDaAcao) {
  const idCampo = useId()
  // input datetime-local pede o formato "AAAA-MM-DDTHH:mm" sem fuso: começa em +1 dia a partir da carência atual.
  const [valor, setValor] = useState(() => {
    const inicio = new Date(Math.max(new Date(ordem.carenciaAte).getTime() + 86_400_000, Date.now() + 3_600_000))
    inicio.setMinutes(inicio.getMinutes() - inicio.getTimezoneOffset())
    return inicio.toISOString().slice(0, 16)
  })
  const ajustar = useComandoDaOrdem(
    ordem.id,
    (data: Date, chave) => comandos.ajustarCarencia(ordem.id, data, chave),
    () => aoConcluir(new Date(valor) <= new Date() ? 'Ordem liberada: já pode ser designada a um técnico.' : 'Carência ajustada no histórico.'),
  )
  return (
    <Acao titulo="Ajustar carência">
      <p className="text-sm text-texto-suave">
        Mude a data em que a ordem sai da carência. Para liberar agora, use o botão acima. Para liberar mais cedo (ou atrasar) escolha a data e hora.
      </p>
      <div className="flex flex-wrap items-end gap-2">
        <label htmlFor={idCampo} className="flex flex-col gap-1 text-sm">
          Liberar em
          <input
            id={idCampo}
            type="datetime-local"
            className={classeDoInput + ' min-w-[220px]'}
            value={valor}
            onChange={(e) => setValor(e.target.value)}
          />
        </label>
        <Button
          type="button"
          tamanho="compacto"
          variante="contorno"
          disabled={ajustar.isPending || !valor}
          onClick={() => ajustar.mutate(new Date(valor))}
        >
          Ajustar
        </Button>
      </div>
      <Falha erro={ajustar.error} />
    </Acao>
  )
}

/** RF08, só o gestor: designa a ordem Disponível a um técnico ativo. */
function Designar({ ordem, aoConcluir }: PropsDaAcao) {
  const idCampo = useId()
  const { data: usuarios } = useQuery(usuariosQuery)
  const tecnicos = (usuarios ?? []).filter((u) => u.papel === 'tecnico' && u.situacao === 'ativo')
  const [tecnico, setTecnico] = useState('')
  const designar = useComandoDaOrdem(ordem.id, (tecnicoId: string, chave) => comandos.designar(ordem.id, tecnicoId, chave), () =>
    aoConcluir('Ordem designada: já aparece no app do técnico.'),
  )

  return (
    <Acao titulo="Designar a um técnico">
      <label htmlFor={idCampo} className="text-sm font-medium">
        Técnico
      </label>
      <select id={idCampo} value={tecnico} onChange={(e) => setTecnico(e.target.value)} className={classeDoInput}>
        <option value="">Escolha um técnico ativo</option>
        {tecnicos.map((t) => (
          <option key={t.id} value={t.id}>
            {t.nome}
          </option>
        ))}
      </select>
      <Button
        type="button"
        tamanho="compacto"
        className="self-start"
        disabled={!tecnico || designar.isPending}
        onClick={() => designar.mutate(tecnico)}
      >
        Designar
      </Button>
      <Falha erro={designar.error} />
    </Acao>
  )
}

/** RF19: o financeiro deu a baixa no HubSoft e marca aqui; a ordem sai da lista "Para dar baixa". */
function MarcarBaixa({ ordem, aoConcluir }: PropsDaAcao) {
  const marcar = useComandoDaOrdem(ordem.id, (_: void, chave) => comandos.marcarBaixa(ordem.id, chave), () =>
    aoConcluir('Baixa marcada como feita.'),
  )
  return (
    <Acao titulo="Baixa no HubSoft">
      <p className="text-sm text-texto-suave">O equipamento já foi retirado. Dê a baixa no HubSoft e marque aqui.</p>
      <Button
        type="button"
        tamanho="compacto"
        className="self-start"
        disabled={marcar.isPending}
        onClick={() => marcar.mutate()}
      >
        Marcar baixa feita
      </Button>
      <Falha erro={marcar.error} />
    </Acao>
  )
}

const motivos: { valor: MotivoDeCancelamento; texto: string }[] = [
  { valor: 'pagou', texto: 'Cliente pagou' },
  { valor: 'renegociou', texto: 'Dívida renegociada' },
  { valor: 'erro', texto: 'Ordem aberta por erro' },
]

/** RF09: cancela com motivo. Com técnico, a ordem some do app dele e a retirada fica bloqueada (RN07). */
function Cancelar({ ordem, aoConcluir }: PropsDaAcao) {
  const [aberto, setAberto] = useState(false)
  const [motivo, setMotivo] = useState<MotivoDeCancelamento>()
  const cancelar = useComandoDaOrdem(ordem.id, (m: MotivoDeCancelamento, chave) => comandos.cancelar(ordem.id, m, chave), () =>
    aoConcluir('Ordem cancelada.'),
  )

  if (!aberto)
    return (
      <Acao titulo="Cancelar a ordem">
        <p className="text-sm text-texto-suave">Quando o cliente pagou, renegociou ou a ordem foi aberta por engano.</p>
        <Button type="button" variante="perigo" tamanho="compacto" className="self-start" onClick={() => setAberto(true)}>
          Cancelar a ordem
        </Button>
      </Acao>
    )

  return (
    <Acao titulo="Cancelar a ordem">
      <fieldset className="flex flex-col gap-1">
        <legend className="mb-1 text-sm font-medium">Motivo</legend>
        {motivos.map((m) => (
          <label key={m.valor} className="flex min-h-11 items-center gap-3 text-sm">
            <input type="radio" name="motivo" value={m.valor} checked={motivo === m.valor} onChange={() => setMotivo(m.valor)} className="size-5 accent-azul" />
            {m.texto}
          </label>
        ))}
      </fieldset>
      {ordem.status === 'atribuida' || ordem.status === 'em_rota' ? (
        <p className="text-sm text-alerta">A ordem está com um técnico: ela sai do app dele na hora.</p>
      ) : null}
      <div className="flex flex-wrap gap-2">
        <Button
          type="button"
          variante="perigo"
          tamanho="compacto"
          disabled={!motivo || cancelar.isPending}
          onClick={() => motivo && cancelar.mutate(motivo)}
        >
          Confirmar cancelamento
        </Button>
        <Button type="button" variante="fantasma" tamanho="compacto" onClick={() => setAberto(false)}>
          Voltar
        </Button>
      </div>
      <Falha erro={cancelar.error} />
    </Acao>
  )
}
