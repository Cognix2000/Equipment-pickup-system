import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { FingerprintPattern, Shield, ShieldCheck, Trash2 } from 'lucide-react'
import { QRCodeSVG } from 'qrcode.react'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { Carregando, FalhaAoCarregar } from '@/components/EstadoDaTela'
import { Button } from '@/components/ui/button'
import { Card, CardTitle } from '@/components/ui/card'
import { classeDoInput } from '@/components/ui/input'
import { acesso, euQuery, rotuloDoPapel, useEu } from '@/features/acesso/acesso'
import { aparelhoAceitaChave, chaveDeAcesso, chavesQuery, ErroDaChave, type ChaveNaLista } from '@/features/acesso/chaveDeAcesso'
import { ErroDaApi } from '@/lib/api'
import { AvisoDeErro } from '@/paginas/acesso/LayoutDeAcesso'

const formatoData = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short', timeZone: 'America/Sao_Paulo' })
const mensagem = (erro: unknown) =>
  erro instanceof ErroDaApi || erro instanceof ErroDaChave ? erro.message : 'Sem resposta do servidor. Confira a conexão.'

/** Minha conta: as chaves de acesso (passkeys) de quem está logado. Cada um só vê e remove as próprias. */
export function MinhaConta() {
  const { data: eu } = useEu()
  const { data: chaves, isPending, isError, error, refetch } = useQuery(chavesQuery)
  const clienteDeConsultas = useQueryClient()
  const [erro, setErro] = useState<string>()

  const cadastrar = useMutation({
    mutationFn: () => chaveDeAcesso.cadastrar(),
    onMutate: () => setErro(undefined),
    onError: (e) => {
      if (!(e instanceof ErroDaChave && e.cancelado)) setErro(mensagem(e))
    },
    onSettled: () => clienteDeConsultas.invalidateQueries({ queryKey: chavesQuery.queryKey }),
  })

  return (
    // No painel, a casca já tem o espaçamento; no app de campo, não.
    <div className={eu?.papel === 'tecnico' ? 'flex flex-col gap-5 p-4' : 'flex flex-col gap-5'}>
      <div>
        <h1 className="text-[28px] font-bold text-marinho">Minha conta</h1>
        {eu && (
          <p className="mt-1 text-[15px] text-texto-suave">
            {eu.nome} · {rotuloDoPapel[eu.papel]}
          </p>
        )}
      </div>

      <SecaoDo2FA />

      <Card className="max-w-[640px]">
        <CardTitle>Chaves de acesso</CardTitle>
        <p className="text-[15px] text-texto-suave">
          Entre com a digital, o rosto ou o PIN do aparelho, sem digitar a senha. A chave fica guardada no aparelho (ou na conta Google,
          Apple ou Microsoft dele) e só funciona neste sistema.
        </p>

        {isPending && <Carregando linhas={2} rotulo="Carregando as chaves" />}
        {isError && <FalhaAoCarregar erro={error} tentarDeNovo={() => void refetch()} />}
        {chaves?.length === 0 && <p className="text-[15px]">Nenhuma chave cadastrada ainda.</p>}
        {chaves && chaves.length > 0 && (
          <ul className="flex flex-col divide-y divide-borda">
            {chaves.map((c) => (
              <LinhaDaChave key={c.id} chave={c} />
            ))}
          </ul>
        )}

        <AvisoDeErro mensagem={erro} />
        {aparelhoAceitaChave() ? (
          <Button type="button" className="self-start" disabled={cadastrar.isPending} onClick={() => cadastrar.mutate()}>
            <FingerprintPattern aria-hidden />
            {cadastrar.isPending ? 'Esperando o aparelho' : 'Cadastrar neste aparelho'}
          </Button>
        ) : (
          <p className="text-[15px]">Este navegador não aceita chave de acesso. Abra o sistema no celular ou num navegador atualizado.</p>
        )}
      </Card>
    </div>
  )
}

/**
 * Ativar/desativar aplicativo autenticador (TOTP) na própria conta (ADR-015).
 * O gestor continua tendo "zerar 2FA de outro usuário" em /gestao/usuarios; esta tela é só a própria conta.
 */
function SecaoDo2FA() {
  const { data: eu } = useEu()
  const clienteDeConsultas = useQueryClient()
  const [ativando, setAtivando] = useState(false)
  const [desativando, setDesativando] = useState(false)
  const [erro, setErro] = useState<string>()

  const desativar = useMutation({
    mutationFn: () => acesso.desativar2FA(),
    onMutate: () => setErro(undefined),
    onSuccess: () => {
      setDesativando(false)
      void clienteDeConsultas.invalidateQueries({ queryKey: euQuery.queryKey })
    },
    onError: (e) => setErro(mensagem(e)),
  })

  return (
    <Card className="max-w-[640px]">
      <CardTitle>Aplicativo autenticador (2FA)</CardTitle>
      <p className="text-[15px] text-texto-suave">
        Código de 6 números pedido ao entrar, gerado por um aplicativo no celular (Google Authenticator, Authy, 1Password…).
        Opcional, mas protege a conta mesmo se a senha vazar.
      </p>

      {eu?.tem2FA ? (
        <>
          <p className="flex items-center gap-2 text-[15px] font-semibold text-sucesso">
            <ShieldCheck aria-hidden className="size-5" /> 2FA ativo: você digita um código ao entrar.
          </p>
          <AvisoDeErro mensagem={erro} />
          {desativando ? (
            <div className="flex flex-wrap items-center gap-2">
              <span className="text-[15px]">Tem certeza? Você vai entrar só com a senha depois disto.</span>
              <Button variante="neutro" tamanho="compacto" onClick={() => { setDesativando(false); setErro(undefined) }}>
                Manter ativo
              </Button>
              <Button variante="perigo" tamanho="compacto" disabled={desativar.isPending} onClick={() => desativar.mutate()}>
                Desativar
              </Button>
            </div>
          ) : (
            <Button type="button" variante="contorno" className="self-start" onClick={() => setDesativando(true)}>
              Desativar o 2FA
            </Button>
          )}
        </>
      ) : (
        <>
          <p className="flex items-center gap-2 text-[15px] text-texto-suave">
            <Shield aria-hidden className="size-5" /> 2FA desativado: você entra só com a senha.
          </p>
          {ativando ? (
            <AtivarPasso aoConcluir={() => { setAtivando(false); void clienteDeConsultas.invalidateQueries({ queryKey: euQuery.queryKey }) }} aoCancelar={() => setAtivando(false)} />
          ) : (
            <Button type="button" className="self-start" onClick={() => setAtivando(true)}>
              Ativar o 2FA
            </Button>
          )}
        </>
      )}
    </Card>
  )
}

const formularioDeCodigo = z.object({ codigo: z.string().regex(/^\d{6}$/, 'O código tem 6 números.') })

function AtivarPasso({ aoConcluir, aoCancelar }: { aoConcluir: () => void; aoCancelar: () => void }) {
  const iniciar = useQuery({
    queryKey: ['acesso', '2fa', 'iniciar-novo'],
    queryFn: () => acesso.iniciar2FA(),
    staleTime: Infinity, // o segredo não pode mudar entre o QR e a confirmação
    retry: false,
  })

  const {
    register, handleSubmit, formState: { errors, isSubmitting },
  } = useForm<z.infer<typeof formularioDeCodigo>>({ resolver: zodResolver(formularioDeCodigo) })

  const [erro, setErro] = useState<string>()
  const confirmar = useMutation({
    mutationFn: (dados: z.infer<typeof formularioDeCodigo>) => acesso.confirmar2FA(dados.codigo),
    onMutate: () => setErro(undefined),
    onSuccess: aoConcluir,
    onError: (e) => setErro(mensagem(e)),
  })

  if (iniciar.isPending) return <Carregando linhas={3} rotulo="Gerando o QR code" />
  if (iniciar.isError) return <FalhaAoCarregar erro={iniciar.error} tentarDeNovo={() => void iniciar.refetch()} />

  return (
    <div className="flex flex-col gap-3 rounded-xl border border-borda-forte p-3">
      <p className="text-[15px]">
        1. Abra o aplicativo autenticador e <strong>escaneie o QR code</strong> abaixo.
      </p>
      <div className="flex items-center justify-center rounded-lg bg-white p-4" aria-label="QR code para o aplicativo autenticador">
        <QRCodeSVG value={iniciar.data.enderecoOtpAuth} size={180} />
      </div>
      <p className="text-[13px] text-texto-suave">
        Se o aparelho não lê o QR, digite este segredo no aplicativo: <code className="font-mono">{iniciar.data.segredo}</code>
      </p>
      <form onSubmit={handleSubmit((d) => confirmar.mutate(d))} className="flex flex-col gap-2">
        <label className="flex flex-col gap-1 text-sm font-semibold">
          2. Digite o código de 6 números que o aplicativo mostra:
          <input
            {...register('codigo')}
            className={classeDoInput}
            inputMode="numeric"
            autoComplete="one-time-code"
            maxLength={6}
            placeholder="000000"
            aria-invalid={!!errors.codigo}
          />
          {errors.codigo && <span className="text-xs text-erro">{errors.codigo.message}</span>}
        </label>
        <AvisoDeErro mensagem={erro} />
        <div className="flex gap-2">
          <Button type="submit" disabled={isSubmitting || confirmar.isPending}>
            Ativar
          </Button>
          <Button type="button" variante="neutro" onClick={aoCancelar}>
            Cancelar
          </Button>
        </div>
      </form>
    </div>
  )
}

function LinhaDaChave({ chave }: { chave: ChaveNaLista }) {
  const clienteDeConsultas = useQueryClient()
  const [confirmando, setConfirmando] = useState(false)
  const remover = useMutation({
    mutationFn: () => chaveDeAcesso.remover(chave.id),
    onSettled: () => clienteDeConsultas.invalidateQueries({ queryKey: chavesQuery.queryKey }),
  })

  return (
    <li className="flex flex-wrap items-center justify-between gap-3 py-3">
      <div className="min-w-0">
        <p className="font-semibold">{chave.nome}</p>
        <p className="text-sm text-texto-suave">
          Cadastrada em {formatoData.format(new Date(chave.criadaEm))}
          {chave.ultimoUsoEm ? ` · último uso em ${formatoData.format(new Date(chave.ultimoUsoEm))}` : ' · ainda não usada'}
        </p>
        {remover.isError && <p className="text-sm text-erro">{mensagem(remover.error)}</p>}
      </div>
      {confirmando ? (
        <div className="flex gap-2">
          <Button variante="neutro" tamanho="compacto" onClick={() => setConfirmando(false)}>
            Manter
          </Button>
          <Button variante="perigo" tamanho="compacto" disabled={remover.isPending} onClick={() => remover.mutate()}>
            Remover de vez
          </Button>
        </div>
      ) : (
        <Button variante="fantasma" tamanho="compacto" aria-label={`Remover a chave ${chave.nome}`} onClick={() => setConfirmando(true)}>
          <Trash2 aria-hidden /> Remover
        </Button>
      )}
    </li>
  )
}
