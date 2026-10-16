import { zodResolver } from '@hookform/resolvers/zod'
import { CircleCheck } from 'lucide-react'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link } from 'react-router'
import { z } from 'zod'
import { Button } from '@/components/ui/button'
import { acesso } from '@/features/acesso/acesso'
import { ErroDaApi } from '@/lib/api'
import { CampoDeSenha } from '@/components/ui/campo-de-senha'
import { AvisoDeErro, Campo, LayoutDeAcesso } from './LayoutDeAcesso'

const formulario = z
  .object({
    senha: z.string().min(12, 'Use pelo menos 12 caracteres.').max(128, 'Use no máximo 128 caracteres.'),
    confirmacao: z.string(),
  })
  .refine((f) => f.senha === f.confirmacao, { message: 'As duas senhas não são iguais.', path: ['confirmacao'] })

/** O token vem depois do # do link: o navegador não o manda para o servidor nem para logs. */
function tokenDoLink() {
  return new URLSearchParams(window.location.hash.slice(1)).get('t') ?? ''
}

/** RF02: quem recebeu o convite define a senha e passa de pendente para ativo. */
export function PrimeiroAcesso() {
  const [token] = useState(tokenDoLink)
  const [pronto, setPronto] = useState(false)
  const [erro, setErro] = useState<string>()
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<z.infer<typeof formulario>>({ resolver: zodResolver(formulario) })

  const enviar = handleSubmit(async ({ senha }) => {
    setErro(undefined)
    try {
      await acesso.aceitarConvite(token, senha)
      history.replaceState(null, '', window.location.pathname) // o token sai da barra de endereço
      setPronto(true)
    } catch (e) {
      setErro(e instanceof ErroDaApi ? e.message : 'Sem resposta do servidor. Confira a conexão.')
    }
  })

  if (!token) {
    return (
      <LayoutDeAcesso titulo="Link incompleto">
        <p className="text-[15px]">Abra o link inteiro que o gestor mandou. Se ele venceu, peça um novo.</p>
      </LayoutDeAcesso>
    )
  }

  if (pronto) {
    return (
      <LayoutDeAcesso titulo="Senha criada">
        <p className="flex items-center gap-2 text-[15px] font-medium text-sucesso">
          <CircleCheck aria-hidden className="size-5" /> Pronto. Agora é só entrar.
        </p>
        <Button asChild tamanho="grande">
          <Link to="/entrar">Ir para o login</Link>
        </Button>
      </LayoutDeAcesso>
    )
  }

  return (
    <LayoutDeAcesso titulo="Crie sua senha" subtitulo="Este link vale uma vez só. Depois, você entra com o seu e-mail e esta senha.">
      <form onSubmit={enviar} noValidate className="flex flex-col gap-4">
        <AvisoDeErro mensagem={erro} />
        <Campo id="senha" rotulo="Nova senha" erro={errors.senha?.message} ajuda="Pelo menos 12 caracteres. Uma frase curta é fácil de lembrar e difícil de adivinhar.">
          <CampoDeSenha
            id="senha"
            autoComplete="new-password"
            aria-invalid={errors.senha ? true : undefined}
            aria-describedby={errors.senha ? 'senha-erro' : 'senha-ajuda'}
            {...register('senha')}
          />
        </Campo>
        <Campo id="confirmacao" rotulo="Repita a senha" erro={errors.confirmacao?.message}>
          <CampoDeSenha
            id="confirmacao"
            autoComplete="new-password"
            aria-invalid={errors.confirmacao ? true : undefined}
            aria-describedby={errors.confirmacao ? 'confirmacao-erro' : undefined}
            {...register('confirmacao')}
          />
        </Campo>
        <Button type="submit" tamanho="grande" disabled={isSubmitting}>
          {isSubmitting ? 'Salvando' : 'Criar senha'}
        </Button>
      </form>
    </LayoutDeAcesso>
  )
}
