import { enviar } from '@/lib/api'
import { ordem } from './tipos'

/** GPS do aparelho no momento da ação (RF12, RF13). */
export type Gps = { latitude: number; longitude: number; precisaoM: number | null }

export type MotivoDaVisita = 'ausente' | 'recusou' | 'nao_achou'

/** RN10: o que aconteceu na ligação. O WhatsApp tem os próprios resultados, registrados pelo financeiro. */
export type ResultadoDaLigacao = 'atendeu' | 'nao_atendeu' | 'caixa_postal' | 'numero_inexistente'

/**
 * Comandos da ordem (ADR-009). A chave de idempotência é criada uma vez por ação na tela e reaproveitada
 * se a pessoa tentar de novo depois de uma falha de rede: o servidor não repete o que já fez.
 */
const comando = (id: string, acao: string, corpo: unknown, chave: string) =>
  enviar(`/api/ordens/${id}/${acao}`, corpo, ordem, { 'Idempotency-Key': chave })

export const comandos = {
  registrarRetirada: (id: string, itens: { equipamentoId: string; numeroSerie: string | null }[], gps: Gps, chave: string) =>
    comando(id, 'retirada', { itens, gps }, chave),
  registrarVisita: (id: string, motivo: MotivoDaVisita, gps: Gps, chave: string) => comando(id, 'visita-sem-sucesso', { motivo, gps }, chave),
  conferir: (id: string, itens: { equipamentoId: string; numeroSerie: string | null }[], chave: string) => comando(id, 'conferencia', { itens }, chave),
  registrarDevolucaoNaLoja: (id: string, chave: string) => comando(id, 'devolucao-na-loja', {}, chave),
  registrarLigacao: (id: string, telefoneId: string, resultado: ResultadoDaLigacao, chave: string) =>
    comando(id, 'contatos', { telefoneId, canal: 'ligacao', resultado }, chave),
}

/** Mesma normalização do servidor (NumeroSerie): sem espaço e em maiúscula. */
export const normalizarSerie = (texto: string) => texto.replace(/\s+/g, '').toUpperCase()

/** Chave nova por ação. Sem contexto seguro (HTTP numa rede local), crypto.randomUUID não existe. */
export function novaChave() {
  if (typeof crypto.randomUUID === 'function') return crypto.randomUUID()
  const b = [...crypto.getRandomValues(new Uint8Array(16))]
  const h = b
    .map((x, i) => (i === 6 ? (x & 0x0f) | 0x40 : i === 8 ? (x & 0x3f) | 0x80 : x)) // versão 4, variante RFC 4122
    .map((x) => x.toString(16).padStart(2, '0'))
    .join('')
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`
}
