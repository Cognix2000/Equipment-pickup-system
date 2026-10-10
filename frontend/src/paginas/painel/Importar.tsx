import { zodResolver } from '@hookform/resolvers/zod'
import { FileSpreadsheet, Upload } from 'lucide-react'
import { useId, useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { Button } from '@/components/ui/button'
import { Card, CardTitle } from '@/components/ui/card'
import {
  descreverTamanho,
  EXTENSOES_ACEITAS,
  formularioDeImportacao,
  type FormularioDeImportacao,
} from '@/features/importacao/validacao'

/**
 * Etapa 1 do UC01: escolher e validar o arquivo no navegador.
 * A prévia (etapa 2) vem do servidor quando o módulo de importação estiver pronto.
 */
export function Importar() {
  const idCampo = useId()
  const [pronto, setPronto] = useState<File | null>(null)
  const {
    control,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormularioDeImportacao>({ resolver: zodResolver(formularioDeImportacao), mode: 'onChange' })

  const enviar = handleSubmit(async ({ arquivo }) => {
    setPronto(arquivo)
  })

  return (
    <>
      <div>
        <h1 className="text-[28px] font-bold text-marinho">Importar planilha</h1>
        <p className="mt-1 text-[15px] text-texto-suave">Confira a prévia antes de gravar. Nada entra sem a sua confirmação.</p>
      </div>

      <Card>
        <form onSubmit={enviar} noValidate className="flex flex-col gap-4">
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
                  onChange={(e) => {
                    setPronto(null)
                    onChange(e.target.files?.[0])
                  }}
                />
              </label>
            )}
          />
          {errors.arquivo && (
            <p id={`${idCampo}-erro`} role="alert" className="rounded-[10px] bg-erro-fundo px-3.5 py-2.5 text-sm text-erro">
              {errors.arquivo.message}
            </p>
          )}
          <Button type="submit" disabled={isSubmitting} className="self-start">
            Conferir o arquivo
          </Button>
        </form>
      </Card>

      {pronto && (
        <Card aria-live="polite">
          <CardTitle>Arquivo pronto para a prévia</CardTitle>
          <div className="flex items-center gap-4">
            <span className="grid size-12 shrink-0 place-items-center rounded-[10px] bg-mapa text-marinho">
              <FileSpreadsheet aria-hidden className="size-6" />
            </span>
            <div className="min-w-0">
              <p className="font-semibold break-all">{pronto.name}</p>
              <p className="text-sm text-texto-suave">{descreverTamanho(pronto.size)} · formato aceito</p>
            </div>
          </div>
        </Card>
      )}
    </>
  )
}
