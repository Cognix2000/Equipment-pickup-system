/**
 * RN10: contato só de segunda a sábado, das 8h às 18h (horário de Brasília). Fora disso, o app
 * não oferece ligar nem enviar mensagem. A janela é o parâmetro "horario_contato" do banco.
 */
const partes = new Intl.DateTimeFormat('en-US', { timeZone: 'America/Sao_Paulo', weekday: 'short', hour: 'numeric', hourCycle: 'h23' })

export function janelaDeContato(agora: Date = new Date()) {
  const valores = Object.fromEntries(partes.formatToParts(agora).map((p) => [p.type, p.value]))
  const hora = Number(valores.hour)
  const permitido = valores.weekday !== 'Sun' && hora >= 8 && hora < 18
  return { permitido, aviso: permitido ? null : 'Contato só de segunda a sábado, das 8h às 18h.' }
}

/** "+5537991234567" → "(37) 99123-4567". */
export function formatarTelefone(e164: string) {
  const d = e164.replace(/^\+55/, '')
  if (d.length === 11) return `(${d.slice(0, 2)}) ${d.slice(2, 7)}-${d.slice(7)}`
  if (d.length === 10) return `(${d.slice(0, 2)}) ${d.slice(2, 6)}-${d.slice(6)}`
  return e164
}
