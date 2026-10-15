import { zodResolver } from '@hookform/resolvers/zod'
import { useQueryClient } from '@tanstack/react-query'
import { QRCodeSVG } from 'qrcode.react'
import { useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useNavigate, useSearchParams } from 'react-router'
import { z } from 'zod'
import { Button } from '@/components/ui/button'
import { acesso, euQuery, inicioDo, useEu, type Papel } from '@/features/acesso/acesso'
import { ErroDaApi } from '@/lib/api'
import { classeDoInput } from '@/components/ui/input'
import { AvisoDeErro, Campo, LayoutDeAcesso } from './LayoutDeAcesso'

type Etapa = 'senha' | 'codigo' | 'configurar'

const formularioDeSenha = z.object({
  email: z.email('Digite um e-mail válido.'),
  senha: z.string().min(1, 'Digite a senha.'),
})

const formularioDeCodigo = z.object({
  codigo: z.string().regex(/^\d{6}$/, 'O código tem 6 números.'),
})

const mensagem = (erro: unknown) => (erro instanceof ErroDaApi ? erro.message : 'Sem resposta do servidor. Confira a conexão.')

/** RF01 e RNF03: senha; depois, para gestor e financeiro, o código do aplicativo autenticador. */
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
      else aoAvancar(resposta.etapa === 'codigo' ? 'codigo' : 'configurar')
    } catch (e) {
      setErro(mensagem(e))
    }
  })

  return (
    <LayoutDeAcesso titulo="Entrar" subtitulo="Use o e-mail e a senha que você cadastrou pelo convite.">
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
          <input
            id="senha"
            type="password"
            autoComplete="current-password"
            className={classeDoInput}
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
    </LayoutDeAcesso>
  )
}

function PassoConfigurar({ aoConcluir }: { aoConcluir: (p: Papel) => Promise<void> }) {
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
    <LayoutDeAcesso titulo="Proteja sua conta" subtitulo="Gestor e financeiro entram com senha e com um código do celular.">
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
    </LayoutDeAcesso>
  )
}
