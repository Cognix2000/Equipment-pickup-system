import type { EventoDaOrdem } from './tipos'

const papeis: Record<string, string> = { admin: 'pelo gestor', financeiro: 'pelo financeiro', tecnico: 'pelo técnico', estoque: 'pelo estoque', sistema: 'automaticamente' }

const motivos: Record<string, string> = {
  ausente: 'cliente ausente',
  recusou: 'cliente recusou',
  nao_achou: 'endereço não encontrado',
  pagou: 'cliente pagou',
  renegociou: 'dívida renegociada',
  erro: 'erro na ordem',
  atendeu: 'cliente atendeu',
  nao_atendeu: 'não atendeu',
  caixa_postal: 'caixa postal',
  numero_inexistente: 'número não existe',
  enviada: 'mensagem enviada',
  sem_whatsapp: 'número sem WhatsApp',
  nao_entregue: 'mensagem não entregue',
  respondeu: 'cliente respondeu',
}

const textos: Record<string, string> = {
  importada: 'Importada da planilha do financeiro',
  liberada: 'Liberada antes do fim da carência',
  carencia_vencida: 'Fim da carência, ficou disponível',
  designada: 'Atribuída a um técnico',
  retirada: 'Retirada registrada',
  visita_sem_sucesso: 'Visita sem sucesso',
  conferida: 'Conferida no estoque',
  divergencia_na_conferencia: 'Divergência na conferência',
  cancelada: 'Cancelada',
  devolvida_na_loja: 'Devolvida na loja',
  devolucao_parcial_na_loja: 'Parte dos itens devolvida na loja',
  baixa_hubsoft: 'Baixa feita no HubSoft',
  atualizada_pela_importacao: 'Atualizada por uma planilha nova',
  contato: 'Contato com o cliente',
  encerrada_sem_resposta: 'Encerrada sem resposta do cliente, vai para a cobrança',
}

/** Uma linha do histórico em português, sem nome de ninguém (só o papel). */
export function descreverEvento(e: EventoDaOrdem) {
  const base = textos[e.tipo] ?? e.tipo
  const motivo = e.motivo ? `: ${motivos[e.motivo] ?? e.motivo}` : ''
  const quem = e.atorPapel && e.tipo !== 'importada' ? ` ${papeis[e.atorPapel] ?? ''}`.trimEnd() : ''
  return `${base}${motivo}${quem}`
}

const formatoDataHora = new Intl.DateTimeFormat('pt-BR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit', timeZone: 'America/Sao_Paulo' })
export const quando = (iso: string) => formatoDataHora.format(new Date(iso)).replace(',', '')
