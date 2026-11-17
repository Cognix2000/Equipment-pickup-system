import { zodResolver } from '@hookform/resolvers/zod'
import { useQueryClient } from '@tanstack/react-query'
import { FingerprintPattern } from 'lucide-react'
import { QRCodeSVG } from 'qrcode.react'
import { useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useNavigate, useSearchParams } from 'react-router'
import { z } from 'zod'
import { Button } from '@/components/ui/button'
import { acesso, euQuery, inicioDo, useEu, type Papel } from '@/features/acesso/acesso'
import { aparelhoAceitaChave, chaveDeAcesso, ErroDaChave } from '@/features/acesso/chaveDeAcesso'
import { ErroDaApi } from '@/lib/api'
import { CampoDeSenha } from '@/components/ui/campo-de-senha'
import { classeDoInput } from '@/components/ui/input'
import { AvisoDeErro, Campo, LayoutDeAcesso } from './LayoutDeAcesso'

// 'chave' saiu em nov/2026 (ADR-015): chave de acesso virou caminho alternativo, não segundo fator.
type Etapa = 'senha' | 'codigo' | 'configurar'

const formularioDeSenha = z.object({
  email: z.email('Digite um e-mail válido.'),
  senha: z.string().min(1, 'Digite a senha.'),
})

const formularioDeCodigo = z.object({
  codigo: z.string().regex(/^\d{6}$/, 'O código tem 6 números.'),
})

const mensagem = (erro: unknown) =>
  erro instanceof ErroDaApi || erro instanceof ErroDaChave ? erro.message : 'Sem resposta do servidor. Confira a conexão.'

/**
 * RF01 e RNF03: senha ou chave de acesso (digital, rosto ou PIN do aparelho). Gestor e financeiro que entram
 * com a senha confirmam com o código do aplicativo ou com a chave; a chave sozinha já vale pelos dois.
 */
export function Entrar() {
  const { data: eu } = useEu()
  const [etapaEscolhida, setEtapa] = useState<Etapa>()
  const clienteDeConsultas = useQueryClient()
  const navegar = useNavigate()
  const [busca] = useSearchParams()

  // Sessão no meio do 2FA (ex.: recarregou a página): continua de onde parou.
  const etapaDaSessao: Etapa = eu?.etapa === 'codigo_2fa' ? 'codigo' : eu?.etapa === 'configurar_2fa' ? 'configurar' : 'senha'
  const etapa = etapaEscolhida ?? etapaDaSessao

  const concluir = async (papel: Papel) => {
    await clienteDeConsultas.invalidateQueries({ queryKey: euQuery.queryKey })
    const volta = busca.get('volta')
    navegar(volta?.startsWith('/') && !volta.startsWith('//') ? volta : inicioDo(papel), { replace: true })
  }

  if (etapa === 'codigo') return <PassoCodigo aoConcluir={concluir} />
  if (etapa === 'configurar') return <PassoConfigurar aoConcluir={concluir} />
  return <PassoSenha aoAvancar={setEtapa} aoConcluir={concluir} />
}

/**
 * Botão da chave de acesso. Some quando o aparelho não aceita (navegador antigo ou sem HTTPS).
 * Cancelar no aparelho não mostra erro: a pessoa só desistiu.
 */
function BotaoDaChave({
  rotulo,
  aoConcluir,
  executar = chaveDeAcesso.entrar,
  variante = 'contorno',
}: {
  rotulo: string
  aoConcluir: (p: Papel) => Promise<void>
  executar?: () => ReturnType<typeof chaveDeAcesso.entrar>
  variante?: 'principal' | 'contorno'
}) {
  const [erro, setErro] = useState<string>()
  const [usando, setUsando] = useState(false)
  if (!aparelhoAceitaChave()) return null

  const usar = async () => {
    setErro(undefined)
    setUsando(true)
    try {
      const resposta = await executar()
      if (resposta.usuario) await aoConcluir(resposta.usuario.papel)
    } catch (e) {
      if (!(e instanceof ErroDaChave && e.cancelado)) setErro(mensagem(e))
    } finally {
      setUsando(false)
    }
  }

  return (
    <div className="flex flex-col gap-3">
      <AvisoDeErro mensagem={erro} />
      <Button type="button" variante={variante} tamanho="grande" disabled={usando} onClick={() => void usar()}>
        <FingerprintPattern aria-hidden />
        {usando ? 'Esperando o aparelho' : rotulo}
      </Button>
    </div>
  )
}

function Ou() {
  return (
    <div className="flex items-center gap-3 text-sm text-texto-suave" aria-hidden>
      <span className="h-px flex-1 bg-borda" />
      ou
      <span className="h-px flex-1 bg-borda" />
    </div>
  )
}

function PassoSenha({ aoAvancar, aoConcluir }: { aoAvancar: (e: Etapa) => void; aoConcluir: (p: Papel) => Promise<void> }) {
  const [erro, setErro] = useState<string>()
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<z.infer<typeof formularioDeSenha>>({ resolver: zodResolver(formularioDeSenha) })

  const enviar = handleSubmit(async ({ email, senha }) => {
    setErro(undefined)
    try {
      const resposta = await acesso.entrar(email, senha)
      if (resposta.etapa === 'pronto' && resposta.usuario) await aoConcluir(resposta.usuario.papel)
      else aoAvancar(resposta.etapa === 'pronto' ? 'senha' : resposta.etapa)
    } catch (e) {
      setErro(mensagem(e))
    }
  })

  return (
    <LayoutDeAcesso titulo="Entrar" subtitulo="Use o e-mail e a senha que você cadastrou pelo convite.">
      {aparelhoAceitaChave() && (
        <>
          <BotaoDaChave rotulo="Entrar com a digital ou o rosto" aoConcluir={aoConcluir} />
          <Ou />
        </>
      )}
      <form onSubmit={enviar} noValidate className="flex flex-col gap-4">
        <AvisoDeErro mensagem={erro} />
        <Campo id="email" rotulo="E-mail" erro={errors.email?.message}>
          <input
            id="email"
            type="email"
            autoComplete="username"
            inputMode="email"
            className={classeDoInput}
            aria-invalid={errors.email ? true : undefined}
            aria-describedby={errors.email ? 'email-erro' : undefined}
            {...register('email')}
          />
        </Campo>
        <Campo id="senha" rotulo="Senha" erro={errors.senha?.message}>
          <CampoDeSenha
            id="senha"
            autoComplete="current-password"
            aria-invalid={errors.senha ? true : undefined}
            aria-describedby={errors.senha ? 'senha-erro' : undefined}
            {...register('senha')}
          />
        </Campo>
        <Button type="submit" tamanho="grande" disabled={isSubmitting}>
          {isSubmitting ? 'Entrando' : 'Entrar'}
        </Button>
        <p className="text-center text-[13px] text-texto-suave">Esqueceu a senha? Peça um novo convite ao gestor.</p>
      </form>
    </LayoutDeAcesso>
  )
}

function CampoDeCodigo({ aoEnviar, rotuloDoBotao }: { aoEnviar: (codigo: string) => Promise<void>; rotuloDoBotao: string }) {
  const [erro, setErro] = useState<string>()
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<z.infer<typeof formularioDeCodigo>>({ resolver: zodResolver(formularioDeCodigo) })

  const enviar = handleSubmit(async ({ codigo }) => {
    setErro(undefined)
    try {
      await aoEnviar(codigo)
    } catch (e) {
      setErro(mensagem(e))
    }
  })

  return (
    <form onSubmit={enviar} noValidate className="flex flex-col gap-4">
      <AvisoDeErro mensagem={erro} />
      <Campo id="codigo" rotulo="Código de 6 números" erro={errors.codigo?.message}>
        <input
          id="codigo"
          inputMode="numeric"
          autoComplete="one-time-code"
          maxLength={6}
          className={`${classeDoInput} text-center font-titulo text-2xl tracking-[0.4em]`}
          aria-invalid={errors.codigo ? true : undefined}
          aria-describedby={errors.codigo ? 'codigo-erro' : undefined}
          {...register('codigo')}
        />
      </Campo>
      <Button type="submit" tamanho="grande" disabled={isSubmitting}>
        {isSubmitting ? 'Conferindo' : rotuloDoBotao}
      </Button>
    </form>
  )
}

function PassoCodigo({ aoConcluir }: { aoConcluir: (p: Papel) => Promise<void> }) {
  return (
    <LayoutDeAcesso titulo="Código de verificação" subtitulo="Abra o aplicativo autenticador no celular e digite o código da Retirada Netwise.">
      <CampoDeCodigo
        rotuloDoBotao="Confirmar"
        aoEnviar={async (codigo) => {
          const resposta = await acesso.confirmarCodigo(codigo)
          if (resposta.usuario) await aoConcluir(resposta.usuario.papel)
        }}
      />
      {aparelhoAceitaChave() && (
        <>
          <Ou />
          <BotaoDaChave rotulo="Usar a chave de acesso" aoConcluir={aoConcluir} />
        </>
      )}
    </LayoutDeAcesso>
  )
}

/**
 * Primeiro acesso de gestor e financeiro. A chave de acesso vem primeiro: não precisa instalar nada.
 * O aplicativo autenticador continua como opção (aparelho sem suporte, ou quem prefere).
 */
function PassoConfigurar({ aoConcluir }: { aoConcluir: (p: Papel) => Promise<void> }) {
  const [comAplicativo, setComAplicativo] = useState(() => !aparelhoAceitaChave())
  if (comAplicativo)
    return <PassoConfigurarAplicativo aoConcluir={aoConcluir} aoVoltar={aparelhoAceitaChave() ? () => setComAplicativo(false) : undefined} />

  return (
    <LayoutDeAcesso titulo="Proteja sua conta" subtitulo="Gestor e financeiro confirmam a entrada com um segundo passo, além da senha.">
      <div className="flex flex-col gap-2 text-[15px]">
        <p className="font-semibold">Use a digital ou o rosto deste aparelho</p>
        <p className="text-texto-suave">
          Não precisa instalar nada. Nas próximas vezes, você entra só com a digital, sem digitar a senha.
        </p>
      </div>
      <BotaoDaChave
        rotulo="Cadastrar a digital ou o rosto"
        variante="principal"
        executar={() => chaveDeAcesso.cadastrar()}
        aoConcluir={aoConcluir}
      />
      <Ou />
      <Button type="button" variante="neutro" tamanho="grande" onClick={() => setComAplicativo(true)}>
        Usar um aplicativo autenticador
      </Button>
    </LayoutDeAcesso>
  )
}

function PassoConfigurarAplicativo({ aoConcluir, aoVoltar }: { aoConcluir: (p: Papel) => Promise<void>; aoVoltar?: () => void }) {
  const [qr, setQr] = useState<{ enderecoOtpAuth: string; segredo: string }>()
  const [erro, setErro] = useState<string>()

  useEffect(() => {
    let ativo = true
    acesso
      .iniciar2FA()
      .then((r) => ativo && setQr(r))
      .catch((e) => ativo && setErro(mensagem(e)))
    return () => {
      ativo = false
    }
  }, [])

  return (
    <LayoutDeAcesso titulo="Proteja sua conta" subtitulo="Gestor e financeiro confirmam a entrada com um código do celular, além da senha.">
      <ol className="flex list-decimal flex-col gap-2 pl-5 text-[15px]">
        <li>Instale um aplicativo autenticador, como Google Authenticator ou Microsoft Authenticator.</li>
        <li>No aplicativo, adicione uma conta lendo o QR code abaixo.</li>
        <li>Digite o código de 6 números que aparecer.</li>
      </ol>
      <AvisoDeErro mensagem={erro} />
      {qr ? (
        <div className="flex flex-col items-center gap-3">
          <div className="rounded-xl border border-borda bg-white p-3">
            <QRCodeSVG value={qr.enderecoOtpAuth} size={184} level="M" title="QR code para o aplicativo autenticador" />
          </div>
          <details className="w-full text-sm">
            <summary className="min-h-11 cursor-pointer content-center text-center font-medium text-azul">Não consegue ler o QR code?</summary>
            <p className="mt-2 text-texto-suave">Digite esta chave no aplicativo:</p>
            <p className="mt-1 rounded-lg bg-fundo px-3 py-2 text-center font-titulo font-bold tracking-wider break-all">{qr.segredo}</p>
          </details>
        </div>
      ) : (
        !erro && <div role="status" aria-label="Gerando o QR code" className="mx-auto size-52 animate-pulse rounded-xl bg-fundo" />
      )}
      <CampoDeCodigo
        rotuloDoBotao="Ativar e entrar"
        aoEnviar={async (codigo) => {
          const resposta = await acesso.confirmar2FA(codigo)
          if (resposta.usuario) await aoConcluir(resposta.usuario.papel)
        }}
      />
      {aoVoltar && (
        <Button type="button" variante="fantasma" onClick={aoVoltar}>
          Prefiro usar a digital ou o rosto
        </Button>
      )}
    </LayoutDeAcesso>
  )
}
