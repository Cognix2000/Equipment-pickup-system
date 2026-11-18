import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { CircleCheck, Download, FileSpreadsheet, Info, Upload } from 'lucide-react'
import { useId, useMemo, useState, type ReactNode } from 'react'
import { Controller, useForm, useWatch } from 'react-hook-form'
import { Link } from 'react-router'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardTitle } from '@/components/ui/card'
import {
  csvDosErros,
  importacao,
  nomeDoFormato,
  rotuloDoResultado,
  type LinhaDaPrevia,
  type Previa,
  type Relatorio,
  type ResultadoDaLinha,
} from '@/features/importacao/api'
import { descreverTamanho, EXTENSOES_ACEITAS, formularioDeImportacao, type FormularioDeImportacao } from '@/features/importacao/validacao'
import { ErroDaApi } from '@/lib/api'
import { cn } from '@/lib/utils'

const mensagem = (erro: unknown) => (erro instanceof ErroDaApi ? erro.message : 'Sem resposta do servidor. Confira a conexão e tente de novo.')

/**
 * UC01 em três etapas: enviar o arquivo, conferir a prévia que o servidor monta e confirmar.
 * Nada é gravado antes da confirmação (RF03), e o relatório mostra o que entrou (RF05).
 */
export function Importar() {
  const [arquivo, setArquivo] = useState<File>()
  const [previa, setPrevia] = useState<Previa>()
  const [relatorio, setRelatorio] = useState<Relatorio>()
  const clienteDeConsultas = useQueryClient()

  const prever = useMutation({ mutationFn: importacao.prever, onSuccess: setPrevia })
  const confirmar = useMutation({
    mutationFn: () => importacao.confirmar(previa!.importacaoId, arquivo!),
    onSuccess: async (r) => {
      setRelatorio(r)
      await clienteDeConsultas.invalidateQueries({ queryKey: ['ordens'] })
    },
  })

  const recomecar = () => {
    if (previa && !relatorio) void importacao.descartar(previa.importacaoId).catch(() => undefined) // só organiza o histórico
    setArquivo(undefined)
    setPrevia(undefined)
    setRelatorio(undefined)
    prever.reset()
    confirmar.reset()
  }

  const etapa = relatorio ? 3 : previa ? 2 : 1

  return (
    <>
      <div>
        <h1 className="text-[28px] font-bold text-marinho">Importar planilha</h1>
        <p className="mt-1 text-[15px] text-texto-suave">
          Aceita qualquer planilha com pelo menos <strong>cliente</strong>, <strong>endereço</strong> e <strong>cidade</strong>. O resto (telefone, bairro, UF, número) entra com padrão e aparece como aviso na prévia. Confira antes de gravar.
        </p>
      </div>

      <ol aria-label="Etapas da importação" className="flex flex-wrap gap-x-6 gap-y-1 text-sm">
        {['Enviar arquivo', 'Conferir a prévia', 'Confirmar e gravar'].map((texto, i) => (
          <li key={texto} aria-current={etapa === i + 1 ? 'step' : undefined} className={cn(etapa === i + 1 ? 'font-semibold text-marinho' : 'text-texto-suave')}>
            {i + 1} · {texto}
          </li>
        ))}
      </ol>

      {etapa === 1 && (
        <EscolhaDoArquivo
          enviando={prever.isPending}
          erro={prever.isError ? mensagem(prever.error) : undefined}
          aoEnviar={(a) => {
            setArquivo(a)
            prever.mutate(a)
          }}
        />
      )}

      {etapa === 2 && previa && arquivo && (
        <PreviaDaImportacao
          previa={previa}
          arquivo={arquivo}
          gravando={confirmar.isPending}
          erro={confirmar.isError ? mensagem(confirmar.error) : undefined}
          aoConfirmar={() => confirmar.mutate()}
          aoTrocar={recomecar}
        />
      )}

      {etapa === 3 && relatorio && <RelatorioDaImportacao relatorio={relatorio} aoImportarOutra={recomecar} />}
    </>
  )
}

function EscolhaDoArquivo({ enviando, erro, aoEnviar }: { enviando: boolean; erro?: string; aoEnviar: (a: File) => void }) {
  const idCampo = useId()
  const {
    control,
    handleSubmit,
    formState: { errors },
  } = useForm<FormularioDeImportacao>({ resolver: zodResolver(formularioDeImportacao), mode: 'onChange' })
  const escolhido = useWatch({ control, name: 'arquivo' })

  return (
    <Card>
      <form onSubmit={handleSubmit(({ arquivo }) => aoEnviar(arquivo))} noValidate className="flex flex-col gap-4">
        <Controller
          control={control}
          name="arquivo"
          render={({ field: { onChange, onBlur, name, ref } }) => (
            <label
              htmlFor={idCampo}
              className="flex cursor-pointer flex-col items-center gap-2 rounded-xl border-2 border-dashed border-azul px-6 py-8 text-center hover:bg-info-fundo/40"
            >
              <Upload aria-hidden className="size-7 text-azul" />
              <span className="font-semibold text-azul">Escolher arquivo</span>
              <span className="max-w-[60ch] text-sm text-texto-suave">
                Aceita {EXTENSOES_ACEITAS.join(', ')} até 10 MB. Macros e fórmulas nunca são executadas.
              </span>
              <input
                id={idCampo}
                ref={ref}
                name={name}
                type="file"
                accept={EXTENSOES_ACEITAS.join(',')}
                className="sr-only"
                aria-invalid={errors.arquivo ? true : undefined}
                aria-describedby={errors.arquivo ? `${idCampo}-erro` : undefined}
                onBlur={onBlur}
                onChange={(e) => onChange(e.target.files?.[0])}
              />
            </label>
          )}
        />
        {escolhido instanceof File && !errors.arquivo && (
          <p className="flex items-center gap-3 text-sm">
            <FileSpreadsheet aria-hidden className="size-5 shrink-0 text-marinho" />
            <span className="min-w-0 font-medium break-all">{escolhido.name}</span>
            <span className="shrink-0 text-texto-suave">{descreverTamanho(escolhido.size)}</span>
          </p>
        )}
        {(errors.arquivo || erro) && (
          <p id={`${idCampo}-erro`} role="alert" className="rounded-[10px] bg-erro-fundo px-3.5 py-2.5 text-sm text-erro">
            {errors.arquivo?.message ?? erro}
          </p>
        )}
        <Button type="submit" disabled={enviando} className="self-start">
          {enviando ? 'Lendo o arquivo' : 'Ver a prévia'}
        </Button>
      </form>
    </Card>
  )
}

type Recorte = 'todas' | ResultadoDaLinha
const recortes: { id: Recorte; texto: string }[] = [
  { id: 'todas', texto: 'Todas' },
  { id: 'nova', texto: 'Novas' },
  { id: 'atualizacao', texto: 'Atualizações' },
  { id: 'pendencia', texto: 'Pendências' },
  { id: 'erro', texto: 'Erros' },
  { id: 'ignorada', texto: 'Ignoradas' },
  { id: 'repetida', texto: 'Sem mudança' },
  { id: 'duplicada', texto: 'Duplicadas' },
]
const PAGINA = 100

function PreviaDaImportacao({
  previa,
  arquivo,
  gravando,
  erro,
  aoConfirmar,
  aoTrocar,
}: {
  previa: Previa
  arquivo: File
  gravando: boolean
  erro?: string
  aoConfirmar: () => void
  aoTrocar: () => void
}) {
  const [recorte, setRecorte] = useState<Recorte>('todas')
  const [limite, setLimite] = useState(PAGINA)
  const { resumo } = previa
  const visiveis = useMemo(() => previa.linhas.filter((l) => recorte === 'todas' || l.resultado === recorte), [previa, recorte])
  const ordensParaGravar = resumo.ordensNovas + resumo.ordensAtualizadas
  const alertas = previa.linhas.reduce((n, l) => n + l.detalhes.filter((d) => d.startsWith('alerta:')).length, 0)

  const baixarErros = () => {
    const url = URL.createObjectURL(new Blob(['﻿' + csvDosErros(previa)], { type: 'text/csv;charset=utf-8' }))
    const link = Object.assign(document.createElement('a'), { href: url, download: `erros-${previa.arquivo.replace(/\.[^.]+$/, '')}.csv` })
    link.click()
    URL.revokeObjectURL(url)
  }

  return (
    <>
      <Card>
        <div className="flex flex-wrap items-center gap-4">
          <span className="grid size-12 shrink-0 place-items-center rounded-[10px] bg-mapa text-marinho">
            <FileSpreadsheet aria-hidden className="size-6" />
          </span>
          <div className="min-w-0 grow">
            <p className="font-semibold break-all">{previa.arquivo}</p>
            <p className="text-sm text-texto-suave">
              {descreverTamanho(arquivo.size)} · {nomeDoFormato[previa.formato]}, reconhecido pelo conteúdo · {previa.totalDeLinhas} linhas lidas
            </p>
          </div>
          <Button variante="neutro" tamanho="compacto" onClick={aoTrocar}>
            Trocar arquivo
          </Button>
        </div>
      </Card>

      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <Numero titulo="Ordens novas" valor={resumo.ordensNovas} tom="text-sucesso" />
        <Numero titulo="Atualizações" valor={resumo.ordensAtualizadas} tom="text-azul" />
        <Numero titulo="Pendências" valor={resumo.pendencias} tom="text-alerta" />
        <Numero titulo="Com erro, ficam fora" valor={resumo.erros} tom="text-erro" />
      </div>

      {(previa.avisos.length > 0 || previa.colunasDescartadas.length > 0 || alertas > 0) && (
        <div className="flex flex-col gap-2">
          {previa.avisos.map((a) => (
            <Aviso key={a}>{a}</Aviso>
          ))}
          {(previa.colunasDescartadas.length > 0 || alertas > 0) && (
            <Aviso>
              {previa.colunasDescartadas.length > 0 &&
                `${previa.colunasDescartadas.length} ${previa.colunasDescartadas.length === 1 ? 'coluna descartada' : 'colunas descartadas'} e nunca gravadas: ${previa.colunasDescartadas.join(', ')}. `}
              {alertas > 0 && `${alertas} ${alertas === 1 ? 'alerta' : 'alertas'} (telefone, CEP ou localização fora do padrão): as linhas entram assim mesmo.`}
            </Aviso>
          )}
        </div>
      )}

      <details className="rounded-xl bg-superficie px-4 py-3 text-sm">
        <summary className="min-h-8 cursor-pointer content-center font-medium text-marinho">
          Colunas reconhecidas · {previa.colunasReconhecidas.length}
        </summary>
        <ul className="mt-2 flex flex-wrap gap-1.5">
          {previa.colunasReconhecidas.map((c) => (
            <li key={c}>
              <Badge tom="info">{c}</Badge>
            </li>
          ))}
        </ul>
      </details>

      <div role="group" aria-label="Filtrar linhas da prévia" className="flex gap-2 overflow-x-auto">
        {recortes.map((r) => {
          const total = r.id === 'todas' ? previa.linhas.length : previa.linhas.filter((l) => l.resultado === r.id).length
          if (total === 0 && r.id !== 'todas') return null
          return (
            <button
              key={r.id}
              type="button"
              aria-pressed={recorte === r.id}
              onClick={() => {
                setRecorte(r.id)
                setLimite(PAGINA)
              }}
              className={cn(
                'min-h-11 shrink-0 rounded-full px-4 text-sm font-medium',
                recorte === r.id ? 'bg-azul text-white' : 'border border-[#c5cdd6] bg-superficie hover:bg-white/60',
              )}
            >
              {r.texto} · {total}
            </button>
          )
        })}
      </div>

      <LinhasDaPrevia linhas={visiveis.slice(0, limite)} />
      {visiveis.length > limite && (
        <div className="flex flex-wrap items-center gap-3 text-sm text-texto-suave">
          Mostrando {limite} de {visiveis.length} linhas
          <Button variante="neutro" tamanho="compacto" onClick={() => setLimite((l) => l + PAGINA)}>
            Mostrar mais {Math.min(PAGINA, visiveis.length - limite)}
          </Button>
        </div>
      )}

      <Card className="gap-4">
        <p className="text-[15px]">
          {resumo.ordensNovas === 1 && 'A ordem nova fica em carência por 48 h. '}
          {resumo.ordensNovas > 1 && `As ${resumo.ordensNovas} ordens novas ficam em carência por 48 h. `}
          {resumo.erros === 1 && 'A linha com erro fica fora: corrija na planilha e envie de novo, sem risco de duplicar.'}
          {resumo.erros > 1 && `As ${resumo.erros} linhas com erro ficam fora: corrija na planilha e envie de novo, sem risco de duplicar.`}
          {ordensParaGravar === 0 && 'Não há nada novo para gravar neste arquivo.'}
        </p>
        {erro && (
          <p role="alert" className="rounded-[10px] bg-erro-fundo px-3.5 py-2.5 text-sm text-erro">
            {erro}
          </p>
        )}
        <div className="flex flex-wrap gap-3">
          <Button onClick={aoConfirmar} disabled={gravando || (ordensParaGravar === 0 && resumo.pendencias === 0)}>
            {gravando ? 'Gravando' : `Gravar ${ordensParaGravar} ${ordensParaGravar === 1 ? 'ordem' : 'ordens'}`}
          </Button>
          {resumo.erros > 0 && (
            <Button variante="neutro" onClick={baixarErros}>
              <Download aria-hidden /> Baixar erros (CSV)
            </Button>
          )}
        </div>
      </Card>
    </>
  )
}

function LinhasDaPrevia({ linhas }: { linhas: LinhaDaPrevia[] }) {
  return (
    <>
      {/* No celular, cada linha vira um cartão; no painel, uma tabela que rola por dentro, nunca a página. */}
      <ul className="flex flex-col gap-3 sm:hidden">
        {linhas.map((l) => (
          <li key={l.numero} className="flex flex-col gap-1.5 rounded-xl bg-superficie p-4">
            <div className="flex items-start justify-between gap-3">
              <p className="font-semibold text-marinho">
                Linha {l.numero}
                {l.cliente && ` · ${l.cliente}`}
              </p>
              <Badge tom={rotuloDoResultado[l.resultado].tom}>{rotuloDoResultado[l.resultado].texto}</Badge>
            </div>
            {l.endereco && (
              <p className="text-sm text-texto-suave">
                {l.endereco}
                {l.bairro && ` · ${l.bairro}`}
              </p>
            )}
            <Detalhes detalhes={l.detalhes} />
          </li>
        ))}
      </ul>

      <Card className="hidden p-0 sm:flex sm:p-0">
        <div className="overflow-x-auto">
          <table className="w-full min-w-[760px] border-collapse text-sm">
            <thead>
              <tr className="bg-fundo text-left">
                {['Linha', 'Cliente', 'Endereço', 'Bairro', 'Tipo da OS', 'Resultado'].map((h) => (
                  <th key={h} scope="col" className="px-3 py-2.5 font-semibold text-texto-suave first:rounded-tl-xl last:rounded-tr-xl">
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {linhas.map((l) => (
                <tr key={l.numero} className="border-t border-borda">
                  <td className="px-3 py-3 align-top tabular-nums">{l.numero}</td>
                  <td className="px-3 py-3 align-top">{l.cliente ?? <span className="text-texto-suave">vazio</span>}</td>
                  <td className="px-3 py-3 align-top">{l.endereco ?? <span className="text-texto-suave">vazio</span>}</td>
                  <td className="px-3 py-3 align-top">{l.bairro ?? <span className="text-texto-suave">vazio</span>}</td>
                  <td className="px-3 py-3 align-top">{l.tipoOs}</td>
                  <td className="px-3 py-3 align-top">
                    <div className="flex flex-col items-start gap-1">
                      <Badge tom={rotuloDoResultado[l.resultado].tom}>{rotuloDoResultado[l.resultado].texto}</Badge>
                      <Detalhes detalhes={l.detalhes} />
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Card>
    </>
  )
}

function Detalhes({ detalhes }: { detalhes: string[] }) {
  if (detalhes.length === 0) return null
  return (
    <ul className="flex flex-col gap-0.5 text-[13px] text-texto-suave">
      {detalhes.map((d) => (
        <li key={d}>· {d}</li>
      ))}
    </ul>
  )
}

function RelatorioDaImportacao({ relatorio, aoImportarOutra }: { relatorio: Relatorio; aoImportarOutra: () => void }) {
  const itens = [
    ['Ordens criadas', relatorio.ordensCriadas],
    ['Ordens atualizadas', relatorio.ordensAtualizadas],
    ['Pendências registradas', relatorio.pendenciasRegistradas],
    ['Pendências resolvidas', relatorio.pendenciasResolvidas],
    ['Linhas com erro, fora', relatorio.resumo.erros],
  ] as const
  return (
    <Card className="gap-4" aria-live="polite">
      <CardTitle className="flex items-center gap-2 text-sucesso">
        <CircleCheck aria-hidden className="size-6" /> Importação gravada
      </CardTitle>
      <dl className="grid grid-cols-2 gap-3 sm:grid-cols-5">
        {itens.map(([texto, valor]) => (
          <div key={texto} className="rounded-xl bg-fundo p-3">
            <dt className="text-[13px] text-texto-suave">{texto}</dt>
            <dd className="font-titulo text-2xl font-bold text-marinho">{valor}</dd>
          </div>
        ))}
      </dl>
      <div className="flex flex-wrap gap-3">
        <Button asChild>
          <Link to="/financeiro/ordens">Ver as ordens</Link>
        </Button>
        <Button variante="neutro" onClick={aoImportarOutra}>
          Importar outra planilha
        </Button>
      </div>
    </Card>
  )
}

function Numero({ titulo, valor, tom }: { titulo: string; valor: number; tom: string }) {
  return (
    <div className="rounded-xl bg-superficie p-4">
      <p className="text-[13px] text-texto-suave">{titulo}</p>
      <p className={cn('font-titulo text-[32px] leading-tight font-bold', tom)}>{valor}</p>
    </div>
  )
}

function Aviso({ children }: { children: ReactNode }) {
  return (
    <p className="flex gap-2 rounded-[10px] bg-info-fundo px-3.5 py-2.5 text-sm text-marinho">
      <Info aria-hidden className="mt-0.5 size-4 shrink-0" />
      <span>{children}</span>
    </p>
  )
}
