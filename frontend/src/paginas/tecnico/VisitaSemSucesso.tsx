import { useMutation, useQueryClient } from '@tanstack/react-query'
import { MapPin } from 'lucide-react'
import { useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { Carregando, FalhaAoCarregar } from '@/components/EstadoDaTela'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { comandos, novaChave, type MotivoDaVisita } from '@/features/ordens/comandos'
import { useOrdem } from '@/features/ordens/consultas'
import { GpsIndisponivel, obterGps } from '@/features/ordens/gps'
import { enderecoCurto } from '@/features/ordens/tipos'
import { ErroDaApi } from '@/lib/api'
import { CabecalhoDaAcao } from './CabecalhoDaAcao'
import { Bloqueio } from './RegistrarRetirada'

const motivos: { valor: MotivoDaVisita; texto: string; detalhe: string }[] = [
  { valor: 'ausente', texto: 'Cliente ausente', detalhe: 'Ninguém atendeu no endereço.' },
  { valor: 'recusou', texto: 'Cliente recusou', detalhe: 'Atendeu, mas não entregou o equipamento.' },
  { valor: 'nao_achou', texto: 'Endereço não encontrado', detalhe: 'O número ou a rua não existem como na ordem.' },
]

/** RF13 e RN09: motivo, GPS e hora. A ordem volta para a fila ou, na 3ª visita, encerra sem retirada. */
export function VisitaSemSucesso() {
  const { id = '' } = useParams()
  const { data: ordem, isPending, isError, error, refetch } = useOrdem(id)
  const navegar = useNavigate()
  const clienteDeConsultas = useQueryClient()
  const [motivo, setMotivo] = useState<MotivoDaVisita>()
  const [chave] = useState(novaChave)

  const registrar = useMutation({
    mutationFn: async () => comandos.registrarVisita(id, motivo!, await obterGps(), chave),
    onSuccess: async (atualizada) => {
      await clienteDeConsultas.invalidateQueries({ queryKey: ['ordens'] })
      navegar('/tecnico/ordens', {
        replace: true,
        state: {
          aviso:
            atualizada.status === 'encerrada_sem_retirada'
              ? 'Visita registrada. Com o limite de visitas sem sucesso, a ordem foi encerrada e vai para a cobrança.'
              : 'Visita registrada. A ordem voltou para a fila.',
        },
      })
    },
  })

  const falha = registrar.error
  const mensagemDeFalha =
    falha instanceof GpsIndisponivel || falha instanceof ErroDaApi ? falha.message : falha ? 'Sem resposta do servidor. Confira o sinal e tente de novo.' : null

  return (
    <>
      <CabecalhoDaAcao
        titulo="Visita sem sucesso"
        subtitulo={ordem ? `OS ${ordem.numeroOs ?? 's/ nº'} · ${enderecoCurto(ordem)}` : undefined}
        voltarPara={`/tecnico/ordens/${id}`}
      />
      <main className="flex flex-1 flex-col gap-3 p-4">
        {isPending && <Carregando linhas={2} rotulo="Carregando a ordem" />}
        {isError && <FalhaAoCarregar erro={error} tentarDeNovo={() => void refetch()} />}
        {ordem && ordem.status !== 'atribuida' && ordem.status !== 'em_rota' && (
          <Bloqueio texto="Esta ordem não está mais aguardando visita com você." voltarPara="/tecnico/ordens" />
        )}
        {ordem && (ordem.status === 'atribuida' || ordem.status === 'em_rota') && (
          <form
            noValidate
            onSubmit={(e) => {
              e.preventDefault()
              registrar.mutate()
            }}
            className="flex flex-col gap-3"
          >
            <Card className="gap-1">
              <fieldset className="flex flex-col gap-1">
                <legend className="mb-2 text-base font-semibold text-marinho">O que aconteceu?</legend>
                {motivos.map((m) => (
                  <label key={m.valor} className="flex min-h-14 items-start gap-3 rounded-[10px] px-2 py-2.5 has-[:checked]:bg-info-fundo">
                    <input
                      type="radio"
                      name="motivo"
                      value={m.valor}
                      checked={motivo === m.valor}
                      onChange={() => setMotivo(m.valor)}
                      className="mt-1 size-5 accent-azul"
                    />
                    <span>
                      <span className="block font-medium">{m.texto}</span>
                      <span className="block text-[13px] text-texto-suave">{m.detalhe}</span>
                    </span>
                  </label>
                ))}
              </fieldset>
            </Card>
            <p className="flex items-center gap-2 text-[13px] text-texto-suave">
              <MapPin aria-hidden className="size-4 shrink-0" />
              GPS e hora entram sozinhos ao confirmar. {ordem.visitasSemSucesso ? `Esta ordem já tem ${ordem.visitasSemSucesso} ${ordem.visitasSemSucesso === 1 ? 'visita' : 'visitas'} sem sucesso.` : ''}
            </p>
            {mensagemDeFalha && (
              <p role="alert" className="rounded-[10px] bg-erro-fundo px-3.5 py-2.5 text-sm text-erro">
                {mensagemDeFalha}
              </p>
            )}
            <Button type="submit" tamanho="grande" disabled={!motivo || registrar.isPending}>
              {registrar.isPending ? 'Registrando' : 'Registrar visita'}
            </Button>
          </form>
        )}
      </main>
    </>
  )
}
