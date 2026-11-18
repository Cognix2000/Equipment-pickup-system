import { z } from 'zod'
import type { TomDoSelo } from '@/components/ui/badge'
import { enviar, enviarArquivo } from '@/lib/api'

// Contrato da importação (Retirada.Ordens/Casos/PreverImportacao.cs e ConfirmarImportacao.cs).
export const resultadoDaLinha = z.enum(['nova', 'atualizacao', 'repetida', 'duplicada', 'pendencia', 'erro', 'ignorada'])
export type ResultadoDaLinha = z.infer<typeof resultadoDaLinha>

const resumo = z.object({
  novas: z.number(),
  atualizacoes: z.number(),
  repetidas: z.number(),
  duplicadas: z.number(),
  pendencias: z.number(),
  erros: z.number(),
  // Ignoradas: linhas que não são de remoção (HubSoft exporta tudo misturado). Não é erro, só não interessa.
  ignoradas: z.number(),
  ordensNovas: z.number(),
  ordensAtualizadas: z.number(),
})

const linha = z.object({
  numero: z.number(),
  resultado: resultadoDaLinha,
  cliente: z.string().nullable(),
  endereco: z.string().nullable(),
  bairro: z.string().nullable(),
  tipoOs: z.string().nullable(),
  detalhes: z.array(z.string()),
})
export type LinhaDaPrevia = z.infer<typeof linha>

export const previa = z.object({
  importacaoId: z.uuid(),
  arquivo: z.string(),
  formato: z.enum(['xlsx', 'xls', 'ods', 'csv', 'json', 'xml']),
  totalDeLinhas: z.number(),
  resumo,
  colunasReconhecidas: z.array(z.string()),
  colunasDescartadas: z.array(z.string()),
  avisos: z.array(z.string()),
  linhas: z.array(linha),
})
export type Previa = z.infer<typeof previa>

export const relatorio = z.object({
  importacaoId: z.uuid(),
  resumo,
  ordensCriadas: z.number(),
  ordensAtualizadas: z.number(),
  pendenciasRegistradas: z.number(),
  pendenciasResolvidas: z.number(),
})
export type Relatorio = z.infer<typeof relatorio>

export const importacao = {
  prever: (arquivo: File) => enviarArquivo('/api/importacoes/previa', arquivo, previa),
  confirmar: (id: string, arquivo: File) => enviarArquivo(`/api/importacoes/${id}/confirmar`, arquivo, relatorio),
  descartar: (id: string) => enviar(`/api/importacoes/${id}/descartar`),
}

export const rotuloDoResultado: Record<ResultadoDaLinha, { texto: string; tom: TomDoSelo }> = {
  nova: { texto: 'Nova', tom: 'sucesso' },
  atualizacao: { texto: 'Atualização', tom: 'info' },
  repetida: { texto: 'Sem mudança', tom: 'neutro' },
  duplicada: { texto: 'Duplicada', tom: 'neutro' },
  pendencia: { texto: 'Pendência', tom: 'alerta' },
  erro: { texto: 'Erro', tom: 'erro' },
  ignorada: { texto: 'Ignorada', tom: 'neutro' },
}

export const nomeDoFormato: Record<Previa['formato'], string> = {
  xlsx: 'Excel',
  xls: 'Excel 97-2003',
  ods: 'LibreOffice',
  csv: 'texto separado',
  json: 'JSON',
  xml: 'XML',
}

/** "Baixar erros (CSV)": linha e motivo, para corrigir na planilha. Separado por ponto e vírgula, como o Excel em português abre. */
export function csvDosErros(p: Previa) {
  // RN06: texto que começa com =, +, - ou @ sai com apóstrofo, para o Excel não executar como fórmula.
  const aspas = (texto: string) => `"${(/^[=+\-@]/.test(texto) ? `'${texto}` : texto).replaceAll('"', '""')}"`
  const linhas = p.linhas
    .filter((l) => l.resultado === 'erro')
    .map((l) => [l.numero, aspas(l.cliente ?? ''), aspas(l.detalhes.filter((d) => !d.startsWith('alerta:')).join('; '))].join(';'))
  return ['linha;cliente;motivo', ...linhas].join('\r\n')
}
