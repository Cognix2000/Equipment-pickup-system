import { describe, expect, it } from 'vitest'
import { formularioDeImportacao, LIMITE_BYTES } from './validacao'

const arquivo = (nome: string, bytes = 100) => new File([new Uint8Array(bytes)], nome)
const mensagem = (f: File) => formularioDeImportacao.safeParse({ arquivo: f }).error?.issues[0]?.message

describe('validação do arquivo de importação', () => {
  it.each(['planilha.xlsx', 'PLANILHA.XLS', 'export.ods', 'consulta.csv', 'bloco de notas.txt', 'api.json', 'api.xml'])(
    'aceita %s',
    (nome) => expect(formularioDeImportacao.safeParse({ arquivo: arquivo(nome) }).success).toBe(true),
  )

  it('recusa .sql com o caminho certo', () => {
    expect(mensagem(arquivo('backup.sql'))).toMatch(/exporte a consulta como \.csv ou \.json/)
  })

  it('recusa PDF', () => {
    expect(mensagem(arquivo('relatorio.pdf'))).toMatch(/Formato não aceito/)
  })

  it('recusa acima de 10 MB e aceita no limite (RN15)', () => {
    expect(mensagem(arquivo('grande.xlsx', LIMITE_BYTES + 1))).toMatch(/10 MB/)
    expect(formularioDeImportacao.safeParse({ arquivo: arquivo('limite.xlsx', LIMITE_BYTES) }).success).toBe(true)
  })

  it('recusa arquivo vazio', () => {
    expect(mensagem(arquivo('vazio.csv', 0))).toBe('O arquivo está vazio.')
  })
})
