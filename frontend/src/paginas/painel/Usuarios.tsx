import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, Copy, UserPlus } from 'lucide-react'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { Carregando, FalhaAoCarregar } from '@/components/EstadoDaTela'
import { Badge, type TomDoSelo } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardTitle } from '@/components/ui/card'
import { linkDeConvite, rotuloDoPapel, usuarioNaLista, usuariosQuery, type LinkDeConvite, type UsuarioNaLista } from '@/features/acesso/acesso'
import { enviar, ErroDaApi } from '@/lib/api'
import { classeDoInput } from '@/components/ui/input'
import { AvisoDeErro, Campo } from '@/paginas/acesso/LayoutDeAcesso'

const formulario = z.object({
  nome: z.string().trim().min(2, 'Digite o nome.'),
  email: z.email('Digite um e-mail válido.'),
  papel: z.enum(['tecnico', 'financeiro', 'estoque', 'admin']),
})

const situacao: Record<UsuarioNaLista['situacao'], { texto: string; tom: TomDoSelo }> = {
  pendente: { texto: 'Aguardando primeiro acesso', tom: 'alerta' },
  ativo: { texto: 'Ativo', tom: 'sucesso' },
  suspenso: { texto: 'Suspenso', tom: 'neutro' },
  revogado: { texto: 'Revogado', tom: 'erro' },
}

const formatoData = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short', timeZone: 'America/Sao_Paulo' })
const exigeDoisFatores = (papel: UsuarioNaLista['papel']) => papel === 'admin' || papel === 'financeiro'

/** RF02, só para o gestor: cadastro por convite, revogação imediata e 2FA. */
export function Usuarios() {
  const { data, isPending, isError, error, refetch } = useQuery(usuariosQuery)
  const [convite, setConvite] = useState<{ nome: string; link: LinkDeConvite }>()

  return (
    <>
      <div>
        <h1 className="text-[28px] font-bold text-marinho">Usuários</h1>
        <p className="mt-1 text-[15px] text-texto-suave">Quem entra no sistema. Revogar corta o acesso na hora.</p>
      </div>

      <div className="flex flex-wrap items-start gap-5">
        <div className="flex min-w-0 grow-[999] basis-[560px] flex-col gap-3">
          {isPending && <Carregando rotulo="Carregando usuários" />}
          {isError && <FalhaAoCarregar erro={error} tentarDeNovo={() => void refetch()} />}
          {data?.map((u) => (
            <LinhaDoUsuario key={u.id} usuario={u} aoConvidar={(link) => setConvite({ nome: u.nome, link })} />
          ))}
        </div>

        <div className="flex min-w-0 grow basis-[320px] flex-col gap-5">
          {convite && <CartaoDoConvite nome={convite.nome} link={convite.link} aoFechar={() => setConvite(undefined)} />}
          <CadastroDeUsuario aoCadastrar={(nome, link) => setConvite({ nome, link })} />
        </div>
      </div>
    </>
  )
}

function LinhaDoUsuario({ usuario: u, aoConvidar }: { usuario: UsuarioNaLista; aoConvidar: (l: LinkDeConvite) => void }) {
  const clienteDeConsultas = useQueryClient()
  const [confirmarRevogacao, setConfirmarRevogacao] = useState(false)
  const acao = useMutation({
    mutationFn: (caminho: string) => enviar(`/api/usuarios/${u.id}/${caminho}`),
    onSuccess: () => clienteDeConsultas.invalidateQueries({ queryKey: usuariosQuery.queryKey }),
  })
  const novoConvite = useMutation({
    mutationFn: () => enviar(`/api/usuarios/${u.id}/convite`, undefined, linkDeConvite),
    onSuccess: aoConvidar,
  })
  const erro = acao.error ?? novoConvite.error
  const { texto, tom } = situacao[u.situacao]

  return (
    <article className="flex flex-col gap-3 rounded-xl bg-superficie p-4" aria-label={u.nome}>
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <h2 className="text-base font-semibold text-marinho">{u.nome}</h2>
          <p className="text-sm break-all text-texto-suave">{u.email}</p>
        </div>
        <span className="flex flex-wrap gap-1.5">
          <Badge tom="info">{rotuloDoPapel[u.papel]}</Badge>
          <Badge tom={tom}>{texto}</Badge>
        </span>
      </div>
      <p className="text-[13px] text-texto-suave">
        {exigeDoisFatores(u.papel) && (u.doisFatores ? '2FA ativo · ' : '2FA configura no primeiro login · ')}
        {u.ultimoLoginEm ? `Último acesso ${formatoData.format(new Date(u.ultimoLoginEm))}` : 'Nunca entrou'}
      </p>
      {erro && <AvisoDeErro mensagem={erro instanceof ErroDaApi ? erro.message : 'Não foi possível concluir.'} />}
      <div className="flex flex-wrap gap-2">
        {u.situacao !== 'revogado' && (
          <Button variante="neutro" tamanho="compacto" onClick={() => novoConvite.mutate()} disabled={novoConvite.isPending}>
            {u.situacao === 'pendente' ? 'Novo convite' : 'Convite para nova senha'}
          </Button>
        )}
        {u.doisFatores && (
          <Button variante="neutro" tamanho="compacto" onClick={() => acao.mutate('zerar-2fa')} disabled={acao.isPending}>
            Zerar 2FA e chaves
          </Button>
        )}
        {u.situacao === 'revogado' ? (
          <Button variante="contorno" tamanho="compacto" onClick={() => acao.mutate('reativar')} disabled={acao.isPending}>
            Reativar
          </Button>
        ) : confirmarRevogacao ? (
          <>
            <Button variante="perigo" tamanho="compacto" onClick={() => acao.mutate('revogar')} disabled={acao.isPending}>
              Confirmar revogação
            </Button>
            <Button variante="fantasma" tamanho="compacto" onClick={() => setConfirmarRevogacao(false)}>
              Manter acesso
            </Button>
          </>
        ) : (
          <Button variante="perigo" tamanho="compacto" onClick={() => setConfirmarRevogacao(true)}>
            Revogar
          </Button>
        )}
      </div>
    </article>
  )
}

function CadastroDeUsuario({ aoCadastrar }: { aoCadastrar: (nome: string, link: LinkDeConvite) => void }) {
  const clienteDeConsultas = useQueryClient()
  const [erro, setErro] = useState<string>()
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<z.infer<typeof formulario>>({ resolver: zodResolver(formulario), defaultValues: { papel: 'tecnico' } })

  const cadastrar = handleSubmit(async (dados) => {
    setErro(undefined)
    try {
      const resposta = await enviar('/api/usuarios', dados, z.object({ usuario: usuarioNaLista, convite: linkDeConvite }))
      aoCadastrar(resposta.usuario.nome, resposta.convite)
      reset()
      await clienteDeConsultas.invalidateQueries({ queryKey: usuariosQuery.queryKey })
    } catch (e) {
      setErro(e instanceof ErroDaApi ? e.message : 'Sem resposta do servidor.')
    }
  })

  return (
    <Card>
      <CardTitle className="flex items-center gap-2">
        <UserPlus aria-hidden className="size-5" /> Cadastrar usuário
      </CardTitle>
      <form onSubmit={cadastrar} noValidate className="flex flex-col gap-4">
        <AvisoDeErro mensagem={erro} />
        <Campo id="nome" rotulo="Nome" erro={errors.nome?.message}>
          <input id="nome" autoComplete="off" className={classeDoInput} aria-invalid={errors.nome ? true : undefined} {...register('nome')} />
        </Campo>
        <Campo id="email-novo" rotulo="E-mail" erro={errors.email?.message}>
          <input id="email-novo" type="email" autoComplete="off" className={classeDoInput} aria-invalid={errors.email ? true : undefined} {...register('email')} />
        </Campo>
        <Campo id="papel" rotulo="Papel" ajuda="Gestor e financeiro confirmam a entrada com a digital do aparelho ou com um aplicativo (2FA).">
          <select id="papel" className={classeDoInput} aria-describedby="papel-ajuda" {...register('papel')}>
            <option value="tecnico">Técnico</option>
            <option value="financeiro">Financeiro</option>
            <option value="estoque">Estoque</option>
            <option value="admin">Gestor</option>
          </select>
        </Campo>
        <Button type="submit" disabled={isSubmitting}>
          {isSubmitting ? 'Cadastrando' : 'Cadastrar e gerar convite'}
        </Button>
      </form>
    </Card>
  )
}

function CartaoDoConvite({ nome, link, aoFechar }: { nome: string; link: LinkDeConvite; aoFechar: () => void }) {
  const [copiado, setCopiado] = useState(false)
  const endereco = `${window.location.origin}${link.caminho}`

  return (
    <Card aria-live="polite" className="border-2 border-azul">
      <CardTitle>Convite de {nome}</CardTitle>
      <p className="text-sm text-texto-suave">
        Mande este link para a pessoa. Vale até {formatoData.format(new Date(link.expiraEm))} e serve uma vez só. Ele não vai aparecer de novo.
      </p>
      <p className="rounded-lg bg-fundo px-3 py-2 text-[13px] break-all">{endereco}</p>
      <div className="flex flex-wrap gap-2">
        <Button
          tamanho="compacto"
          onClick={async () => {
            await navigator.clipboard.writeText(endereco)
            setCopiado(true)
          }}
        >
          {copiado ? <Check aria-hidden /> : <Copy aria-hidden />}
          {copiado ? 'Copiado' : 'Copiar link'}
        </Button>
        <Button variante="fantasma" tamanho="compacto" onClick={aoFechar}>
          Fechar
        </Button>
      </div>
    </Card>
  )
}
