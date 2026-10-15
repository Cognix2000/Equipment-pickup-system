import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { classeDoInput } from '@/components/ui/input'
import { comandos, type ModeloDeMensagem, type ResultadoDaLigacao, type ResultadoDaMensagem } from './comandos'
import { formatarTelefone } from './contato'
import type { Telefone } from './tipos'
import { mensagemDaFalha, useComandoDaOrdem } from './useComandoDaOrdem'

const daLigacao: { valor: ResultadoDaLigacao; texto: string }[] = [
  { valor: 'atendeu', texto: 'Atendeu' },
  { valor: 'nao_atendeu', texto: 'Não atendeu' },
  { valor: 'caixa_postal', texto: 'Caixa postal' },
  { valor: 'numero_inexistente', texto: 'Número não existe' },
]

const daMensagem: { valor: ResultadoDaMensagem; texto: string }[] = [
  { valor: 'enviada', texto: 'Enviada' },
  { valor: 'respondeu', texto: 'Cliente respondeu' },
  { valor: 'nao_entregue', texto: 'Não entregue' },
  { valor: 'sem_whatsapp', texto: 'Sem WhatsApp' },
]

const modelos: { valor: ModeloDeMensagem; texto: string }[] = [
  { valor: 'aviso', texto: 'Aviso de retirada' },
  { valor: 'lembrete', texto: 'Lembrete' },
  { valor: 'confirmacao', texto: 'Confirmação da visita' },
  { valor: 'ausente', texto: 'Cliente ausente na visita' },
]

type Props = { ordemId: string; telefone: Telefone; canal: 'ligacao' | 'whatsapp'; aoRegistrar: () => void }

/**
 * RN10: registra como foi o contato com o cliente. Cada resultado é um botão: um toque e está registrado.
 * O registro conta os dias sem resposta (RN09) e marca o telefone (número inexistente, sem WhatsApp...).
 */
export function RegistroDeContato({ ordemId, telefone, canal, aoRegistrar }: Props) {
  const [modelo, setModelo] = useState<ModeloDeMensagem>('aviso')
  const registrar = useComandoDaOrdem(ordemId, (resultado: string, chave) =>
    comandos.registrarContato(
      ordemId,
      canal === 'ligacao'
        ? { telefoneId: telefone.id, canal, resultado: resultado as ResultadoDaLigacao }
        : { telefoneId: telefone.id, canal, resultado: resultado as ResultadoDaMensagem, modelo },
      chave,
    ),
    aoRegistrar,
  )
  const idTitulo = `contato-${canal}-${telefone.id}`
  const idModelo = `modelo-${telefone.id}`
  const numero = formatarTelefone(telefone.numero)
  const falha = mensagemDaFalha(registrar.error)

  return (
    <div role="group" aria-labelledby={idTitulo} className="flex flex-col gap-2 rounded-[10px] bg-fundo p-3">
      <p id={idTitulo} className="text-sm font-semibold text-marinho">
        {canal === 'ligacao' ? `Como foi a ligação para ${numero}?` : `Como foi a mensagem para ${numero}?`}
      </p>
      {canal === 'whatsapp' && (
        <div className="flex flex-col gap-1">
          <label htmlFor={idModelo} className="text-[13px] font-medium">
            Modelo enviado
          </label>
          <select id={idModelo} value={modelo} onChange={(e) => setModelo(e.target.value as ModeloDeMensagem)} className={classeDoInput}>
            {modelos.map((m) => (
              <option key={m.valor} value={m.valor}>
                {m.texto}
              </option>
            ))}
          </select>
        </div>
      )}
      <div className="grid grid-cols-2 gap-2">
        {(canal === 'ligacao' ? daLigacao : daMensagem).map((r) => (
          <Button
            key={r.valor}
            type="button"
            variante="neutro"
            tamanho="compacto"
            disabled={registrar.isPending}
            onClick={() => registrar.mutate(r.valor)}
          >
            {r.texto}
          </Button>
        ))}
      </div>
      {falha && (
        <p role="alert" className="text-sm text-erro">
          {falha}
        </p>
      )}
    </div>
  )
}
