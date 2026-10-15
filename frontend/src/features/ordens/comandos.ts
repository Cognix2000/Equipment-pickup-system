import { enviar } from '@/lib/api'
import { ordem } from './tipos'

/** GPS do aparelho no momento da ação (RF12, RF13). */
export type Gps = { latitude: number; longitude: number; precisaoM: number | null }

export type MotivoDaVisita = 'ausente' | 'recusou' | 'nao_achou'

/** RN14: foto já enviada, que entra na retirada pelo id que o servidor devolveu. */
export type FotoDaRetirada = { id: string; tipo: 'equipamento' | 'etiqueta_serie'; equipamentoId: string | null; capturadaEm: string }

export type MotivoDeCancelamento = 'pagou' | 'renegociou' | 'erro'

/** RN10: o que aconteceu na ligação. O WhatsApp tem os próprios resultados. */
export type ResultadoDaLigacao = 'atendeu' | 'nao_atendeu' | 'caixa_postal' | 'numero_inexistente'
export type ResultadoDaMensagem = 'enviada' | 'sem_whatsapp' | 'nao_entregue' | 'respondeu'
export type ModeloDeMensagem = 'aviso' | 'lembrete' | 'confirmacao' | 'ausente'

/** O sistema não liga nem manda mensagem: só registra o que a pessoa fez pelo celular ou pelo WhatsApp oficial. */
export type Contato =
  | { telefoneId: string; canal: 'ligacao'; resultado: ResultadoDaLigacao }
  | { telefoneId: string; canal: 'whatsapp'; resultado: ResultadoDaMensagem; modelo: ModeloDeMensagem }

/**
 * Comandos da ordem (ADR-009). A chave de idempotência é criada uma vez por ação na tela e reaproveitada
 * se a pessoa tentar de novo depois de uma falha de rede: o servidor não repete o que já fez.
 */
const comando = (id: string, acao: string, corpo: unknown, chave: string) =>
  enviar(`/api/ordens/${id}/${acao}`, corpo, ordem, { 'Idempotency-Key': chave })

export const comandos = {
  registrarRetirada: (
    id: string,
    itens: { equipamentoId: string; numeroSerie: string | null }[],
    gps: Gps,
    fotos: FotoDaRetirada[],
    chave: string,
  ) => comando(id, 'retirada', { itens, gps, fotos }, chave),
  registrarVisita: (id: string, motivo: MotivoDaVisita, gps: Gps, chave: string) => comando(id, 'visita-sem-sucesso', { motivo, gps }, chave),
  conferir: (id: string, itens: { equipamentoId: string; numeroSerie: string | null }[], chave: string) => comando(id, 'conferencia', { itens }, chave),
  registrarDevolucaoNaLoja: (id: string, chave: string) => comando(id, 'devolucao-na-loja', {}, chave),
  registrarContato: (id: string, contato: Contato, chave: string) => comando(id, 'contatos', contato, chave),
  liberar: (id: string, chave: string) => comando(id, 'liberar', {}, chave),
  designar: (id: string, tecnicoId: string, chave: string) => comando(id, 'designar', { tecnicoId }, chave),
  cancelar: (id: string, motivo: MotivoDeCancelamento, chave: string) => comando(id, 'cancelar', { motivo }, chave),
  marcarBaixa: (id: string, chave: string) => comando(id, 'baixa-hubsoft', {}, chave),
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
