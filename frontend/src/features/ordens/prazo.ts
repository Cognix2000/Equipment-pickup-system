const formatoRelativo = new Intl.RelativeTimeFormat('pt-BR', { numeric: 'auto' })
const umDia = 24 * 60 * 60 * 1000

export type Prazo = { texto: string; vencido: boolean }

/** O prazo de devolução só importa enquanto o equipamento ainda está com o cliente. */
export const statusComEquipamentoNoCliente = new Set(['nova', 'disponivel', 'atribuida', 'em_rota', 'nao_concluida'])

/**
 * Prazo de devolução em linguagem do dia a dia: "Prazo venceu há 1 dia", "Prazo hoje", "Prazo amanhã".
 * Conta por dia de calendário no horário de Brasília (RNF09), não por blocos de 24 h.
 */
export function descreverPrazo(limiteIso: string, agora: Date = new Date()): Prazo {
  const dias = diaDeCalendario(new Date(limiteIso)) - diaDeCalendario(agora)

  if (dias < 0) {
    const n = Math.abs(dias)
    return { texto: `Prazo venceu há ${n} ${n === 1 ? 'dia' : 'dias'}`, vencido: true }
  }
  if (dias === 0) return { texto: 'Prazo hoje', vencido: false }
  return { texto: `Prazo ${formatoRelativo.format(dias, 'day')}`, vencido: false }
}

const formatoDia = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Sao_Paulo', year: 'numeric', month: '2-digit', day: '2-digit' })

function diaDeCalendario(data: Date) {
  return Math.round(Date.parse(formatoDia.format(data)) / umDia)
}
