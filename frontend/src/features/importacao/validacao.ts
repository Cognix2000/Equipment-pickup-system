import { z } from 'zod'

/** RN15: o único limite é o tamanho, checado antes de ler o conteúdo. Configurável no servidor. */
export const LIMITE_BYTES = 10 * 1024 * 1024

/** Formatos aceitos (seção "Formatos aceitos na importação"). .sql e PDF ficam fora de propósito. */
export const EXTENSOES_ACEITAS = ['.xlsx', '.xls', '.ods', '.csv', '.txt', '.json', '.xml'] as const

export function extensaoDe(nome: string) {
  const ponto = nome.lastIndexOf('.')
  return ponto < 0 ? '' : nome.slice(ponto).toLowerCase()
}

export const formularioDeImportacao = z.object({
  arquivo: z.instanceof(File, { message: 'Escolha o arquivo da planilha.' }).superRefine((arquivo, ctx) => {
    const extensao = extensaoDe(arquivo.name)
    const erro = (message: string) => ctx.addIssue({ code: 'custom', message })

    if (!(EXTENSOES_ACEITAS as readonly string[]).includes(extensao)) {
      erro(
        extensao === '.sql'
          ? 'Arquivo .sql não é aceito: exporte a consulta como .csv ou .json.'
          : `Formato não aceito. Use ${EXTENSOES_ACEITAS.join(', ')}.`,
      )
    } else if (arquivo.size === 0) {
      erro('O arquivo está vazio.')
    } else if (arquivo.size > LIMITE_BYTES) {
      erro('O arquivo passa de 10 MB. Divida a planilha em partes menores.')
    }
  }),
})

export type FormularioDeImportacao = z.infer<typeof formularioDeImportacao>

const formatoTamanho = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 1 })

export function descreverTamanho(bytes: number) {
  if (bytes < 1024) return 'menos de 1 KB'
  if (bytes < 1024 * 1024) return `${formatoTamanho.format(bytes / 1024)} KB`
  return `${formatoTamanho.format(bytes / 1024 / 1024)} MB`
}
