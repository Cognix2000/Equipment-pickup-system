import { z } from 'zod'
import type { TomDoSelo } from '@/components/ui/badge'

// Contrato da API de ordens (Retirada.Ordens/ModuloOrdens.cs). Os textos batem com o banco.
export const statusOrdem = z.enum([
  'nova',
  'disponivel',
  'atribuida',
  'em_rota',
  'concluida',
  'conferida',
  'nao_concluida',
  'cancelada',
  'devolvida_na_loja',
  'encerrada_sem_retirada',
])
export type StatusOrdem = z.infer<typeof statusOrdem>

const tipoEquipamento = z.enum(['ont', 'onu', 'roteador', 'repetidor_mesh', 'fonte'])
export type TipoEquipamento = z.infer<typeof tipoEquipamento>

const equipamento = z.object({
  id: z.uuid(),
  tipo: tipoEquipamento,
  modelo: z.string(),
  numeroSerie: z.string().nullable(),
  situacao: z.enum(['pendente', 'retirado', 'devolvido_na_loja', 'conferido', 'divergente', 'extraviado']),
})

const telefone = z.object({
  id: z.uuid(),
  numero: z.string(),
  tipo: z.enum(['celular', 'fixo', 'desconhecido']),
  formatoValido: z.boolean(),
  // Vêm do resultado dos contatos registrados (RN10).
  linha: z.enum(['nao_verificada', 'atende', 'inexistente']),
  whatsapp: z.enum(['nao_verificado', 'tem', 'nao_tem', 'nao_entregue']),
})
export type Telefone = z.infer<typeof telefone>

const evento = z.object({
  tipo: z.string(),
  ocorridoEm: z.iso.datetime({ offset: true }),
  motivo: z.string().nullable(),
  statusPara: z.string().nullable(),
  atorPapel: z.string().nullable(),
})
export type EventoDaOrdem = z.infer<typeof evento>

export const tipoDeFoto = z.enum(['equipamento', 'etiqueta_serie', 'comprovante_loja'])
export type TipoDeFoto = z.infer<typeof tipoDeFoto>

/** RN14: foto da ordem. O arquivo sai em /api/ordens/{id}/fotos/{fotoId}, com a permissão conferida. */
const foto = z.object({
  id: z.uuid(),
  tipo: tipoDeFoto,
  equipamentoId: z.uuid().nullable(),
  capturadaEm: z.iso.datetime({ offset: true }),
})
export type FotoDaOrdem = z.infer<typeof foto>

export const ordem = z.object({
  id: z.uuid(),
  numeroOs: z.number().int().nullable(),
  tipoOs: z.string(),
  status: statusOrdem,
  cliente: z.string(),
  endereco: z.object({
    logradouro: z.string(),
    numero: z.string().nullable(),
    complemento: z.string().nullable(),
    bairro: z.string(),
    cidade: z.string(),
    uf: z.string(),
    referencia: z.string().nullable(),
  }),
  localizacao: z.object({ latitude: z.number(), longitude: z.number() }).nullable(),
  observacao: z.string().nullable(),
  // Plano de internet do cliente (ex.: "600 Mbps", "1 Gbps"). Extraído da descrição do HubSoft; null se desconhecido.
  plano: z.string().nullable(),
  carenciaAte: z.iso.datetime({ offset: true }),
  responsavelId: z.uuid().nullable().optional(),
  visitasSemSucesso: z.number().optional(),
  aguardaBaixaNoHubsoft: z.boolean(),
  equipamentos: z.array(equipamento),
  // Só o detalhe traz (RF07); na lista vêm nulos.
  telefones: z.array(telefone).nullish(),
  historico: z.array(evento).nullish(),
  fotos: z.array(foto).nullish(),
  // Só na lista (RN09): passou do prazo de resposta sem nenhuma tentativa de contato.
  semContatoNoPrazo: z.boolean().nullish(),
})
export type Ordem = z.infer<typeof ordem>
export type Equipamento = z.infer<typeof equipamento>

export const listaDeOrdens = z.array(ordem)

/** "ONU Huawei EG8010H", mas "Fonte 12V" (sem repetir o tipo quando o modelo já diz). */
export function nomeDoItem(e: Equipamento) {
  const tipo = rotuloDoTipo[e.tipo]
  return e.modelo.toLowerCase().startsWith(tipo.toLowerCase()) ? e.modelo : `${tipo} ${e.modelo}`
}

/** O que ainda está com o cliente: nem retirado, nem devolvido na loja (RF15). É o que o técnico recolhe. */
export const comOCliente = (o: Ordem) => o.equipamentos.filter((e) => e.situacao === 'pendente')
export const devolvidosNaLoja = (o: Ordem) => o.equipamentos.filter((e) => e.situacao === 'devolvido_na_loja')

export const rotuloDoStatus: Record<StatusOrdem, { texto: string; tom: TomDoSelo }> = {
  nova: { texto: 'Em carência', tom: 'neutro' },
  disponivel: { texto: 'Disponível', tom: 'info' },
  atribuida: { texto: 'Atribuída', tom: 'destaque' },
  em_rota: { texto: 'Em rota', tom: 'destaque' },
  concluida: { texto: 'Concluída', tom: 'info' },
  conferida: { texto: 'Conferida', tom: 'sucesso' },
  nao_concluida: { texto: 'Não concluída', tom: 'alerta' },
  cancelada: { texto: 'Cancelada', tom: 'erro' },
  devolvida_na_loja: { texto: 'Devolvida na loja', tom: 'sucesso' },
  encerrada_sem_retirada: { texto: 'Encerrada sem retirada', tom: 'alerta' },
}

export const rotuloDoTipo: Record<TipoEquipamento, string> = {
  ont: 'ONT',
  onu: 'ONU',
  roteador: 'Roteador',
  repetidor_mesh: 'Repetidor Mesh',
  fonte: 'Fonte',
}

/** "Rua dos Ipês, 120": a planilha vem em caixa alta; a tela mostra em caixa de título. */
export function enderecoCurto(o: Ordem) {
  const rua = caixaDeTitulo(o.endereco.logradouro)
  return o.endereco.numero ? `${rua}, ${o.endereco.numero}` : rua
}

const minusculas = new Set(['da', 'das', 'de', 'do', 'dos', 'e'])

export function caixaDeTitulo(texto: string) {
  return texto
    .toLocaleLowerCase('pt-BR')
    .split(' ')
    .map((p, i) => (i > 0 && minusculas.has(p) ? p : p.charAt(0).toLocaleUpperCase('pt-BR') + p.slice(1)))
    .join(' ')
}

/** Texto curto do que recolher: "ONU + Roteador + Fonte". */
export function resumoDosItens(o: Ordem) {
  return o.equipamentos.map((e) => rotuloDoTipo[e.tipo]).join(' + ')
}
