import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { FingerprintPattern, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { Carregando, FalhaAoCarregar } from '@/components/EstadoDaTela'
import { Button } from '@/components/ui/button'
import { Card, CardTitle } from '@/components/ui/card'
import { rotuloDoPapel, useEu } from '@/features/acesso/acesso'
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
