import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation } from '@tanstack/react-query'
import { Plus, Trash2 } from 'lucide-react'
import { useMemo, useState } from 'react'
import { useFieldArray, useForm } from 'react-hook-form'
import { Link, useNavigate } from 'react-router'
import { Button } from '@/components/ui/button'
import { Card, CardTitle } from '@/components/ui/card'
import { classeDoInput } from '@/components/ui/input'
import { cadastrarOrdemManual, novaChave, type PedidoDeNovaOrdem } from '@/features/ordens/comandos'
import { ErroDaApi } from '@/lib/api'
import { z } from 'zod'

const tipoEquipamentoForm = z.enum(['ONT', 'ONU', 'Roteador', 'RepetidorMesh', 'Fonte'])

const equipamentoForm = z.object({
  tipo: tipoEquipamentoForm,
  modelo: z.string().trim().min(1, 'Modelo é obrigatório'),
  serie: z.string().trim().optional(),
  mac: z.string().trim().optional(),
  valor: z.string().trim().optional(),
})

const formularioDeNovaOrdem = z.object({
  cliente: z.string().trim().min(1, 'Nome do cliente é obrigatório'),
  codigoClienteHubsoft: z.string().trim().optional(),
  observacao: z.string().trim().optional(),
  logradouro: z.string().trim().min(1, 'Logradouro é obrigatório'),
  numero: z.string().trim().optional(),
  complemento: z.string().trim().optional(),
  bairro: z.string().trim().optional(),
  cidade: z.string().trim().min(1, 'Cidade é obrigatória'),
  uf: z.string().trim().length(2, 'UF com 2 letras').toUpperCase(),
  referencia: z.string().trim().optional(),
  cep: z.string().trim().optional(),
  telefone1: z.string().trim().optional(),
  telefone2: z.string().trim().optional(),
  tipoOs: z.string().trim().optional(),
  carenciaHoras: z.string().trim().optional(),
  equipamentos: z.array(equipamentoForm).min(1, 'Adicione pelo menos um equipamento'),
})
type FormularioDeNovaOrdem = z.infer<typeof formularioDeNovaOrdem>

/** Converte o valor do formulário (string) em número positivo; vazio ou inválido vira null. */
function numeroOuNull(v?: string): number | null {
  if (!v) return null
  const n = Number(v.replace(',', '.'))
  return Number.isFinite(n) && n >= 0 ? n : null
}
function inteiroOuNull(v?: string): number | null {
  if (!v) return null
  const n = Number.parseInt(v, 10)
  return Number.isFinite(n) && n > 0 ? n : null
}
function strOuNull(v?: string): string | null {
  return v && v.length > 0 ? v : null
}

/**
 * Cadastro manual pelo financeiro (RF03 estendido). Mesmas regras da planilha, só sem o arquivo:
 * ordem nasce Nova e em carência; o botão fica no menu só quando o papel é financeiro ou admin.
 */
export function NovaOrdem() {
  const navegar = useNavigate()
  const [chave] = useState(novaChave) // uma chave por tela: reenvio depois de falha de rede não cria outra ordem

  const {
    register, handleSubmit, control, formState: { errors, isSubmitting },
  } = useForm<FormularioDeNovaOrdem>({
    resolver: zodResolver(formularioDeNovaOrdem),
    defaultValues: {
      uf: 'MG',
      tipoOs: 'RETIRADA DE EQUIPAMENTO',
      carenciaHoras: '48',
      equipamentos: [{ tipo: 'ONT', modelo: '' }],
    },
  })
  const equipamentos = useFieldArray({ control, name: 'equipamentos' })

  const envio = useMutation({
    mutationFn: (dados: FormularioDeNovaOrdem) => {
      const pedido: PedidoDeNovaOrdem = {
        cliente: dados.cliente,
        codigoClienteHubsoft: inteiroOuNull(dados.codigoClienteHubsoft),
        observacao: strOuNull(dados.observacao),
        endereco: {
          logradouro: dados.logradouro,
          numero: strOuNull(dados.numero),
          complemento: strOuNull(dados.complemento),
          bairro: strOuNull(dados.bairro),
          cidade: dados.cidade,
          uf: dados.uf,
          referencia: strOuNull(dados.referencia),
        },
        cep: strOuNull(dados.cep),
        telefones: [dados.telefone1, dados.telefone2].filter((t): t is string => !!t && t.trim().length > 0),
        equipamentos: dados.equipamentos.map((e) => ({
          tipo: e.tipo,
          modelo: e.modelo,
          serie: strOuNull(e.serie),
          mac: strOuNull(e.mac),
          valor: numeroOuNull(e.valor),
        })),
        tipoOs: strOuNull(dados.tipoOs),
        carenciaHoras: inteiroOuNull(dados.carenciaHoras),
      }
      return cadastrarOrdemManual(pedido, chave)
    },
    onSuccess: (ordem) => navegar(`/financeiro/ordens/${ordem.id}`),
  })

  const mensagemDeErro = useMemo(() => {
    const e = envio.error
    if (!e) return null
    if (e instanceof ErroDaApi) return e.message
    return 'Sem resposta do servidor. Confira a conexão e tente de novo.'
  }, [envio.error])

  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-col gap-1">
        <h1 className="font-titulo text-2xl font-bold">Cadastrar ordem</h1>
        <p className="text-[15px] text-texto-suave">
          Cria a ordem direto, sem planilha. A ordem nasce <strong>em carência</strong>, igual à da importação, e fica
          disponível para o técnico depois do prazo. Para várias ordens de uma vez, use <Link to="/financeiro/importar" className="text-azul no-underline">Importar</Link>.
        </p>
      </div>

      <form onSubmit={handleSubmit((d) => envio.mutate(d))} className="flex flex-col gap-5">
        <Card>
          <CardTitle>Cliente</CardTitle>
          <div className="mt-3 grid grid-cols-1 gap-3 md:grid-cols-[2fr_1fr]">
            <Campo rotulo="Nome do cliente" erro={errors.cliente?.message}>
              <input {...register('cliente')} className={classeDoInput} placeholder="Ex.: ASSINANTE SIMULADO 7" />
            </Campo>
            <Campo rotulo="Código no HubSoft (opcional)" erro={errors.codigoClienteHubsoft?.message}>
              <input {...register('codigoClienteHubsoft')} className={classeDoInput} inputMode="numeric" placeholder="Ex.: 12345" />
            </Campo>
          </div>
          <div className="mt-3 grid grid-cols-1 gap-3 md:grid-cols-2">
            <Campo rotulo="Telefone 1 (opcional)" erro={errors.telefone1?.message}>
              <input {...register('telefone1')} className={classeDoInput} inputMode="tel" placeholder="(00) 98765-4321" />
            </Campo>
            <Campo rotulo="Telefone 2 (opcional)" erro={errors.telefone2?.message}>
              <input {...register('telefone2')} className={classeDoInput} inputMode="tel" />
            </Campo>
          </div>
        </Card>

        <Card>
          <CardTitle>Endereço</CardTitle>
          <div className="mt-3 grid grid-cols-1 gap-3 md:grid-cols-[3fr_1fr_1fr]">
            <Campo rotulo="Logradouro" erro={errors.logradouro?.message}>
              <input {...register('logradouro')} className={classeDoInput} placeholder="Ex.: RUA DOS IPÊS" />
            </Campo>
            <Campo rotulo="Número" erro={errors.numero?.message}>
              <input {...register('numero')} className={classeDoInput} placeholder="120 ou s/n" />
            </Campo>
            <Campo rotulo="Complemento" erro={errors.complemento?.message}>
              <input {...register('complemento')} className={classeDoInput} placeholder="Apto 203" />
            </Campo>
          </div>
          <div className="mt-3 grid grid-cols-1 gap-3 md:grid-cols-[2fr_2fr_1fr_1fr]">
            <Campo rotulo="Bairro" erro={errors.bairro?.message}>
              <input {...register('bairro')} className={classeDoInput} placeholder="CENTRO" />
            </Campo>
            <Campo rotulo="Cidade" erro={errors.cidade?.message}>
              <input {...register('cidade')} className={classeDoInput} placeholder="CIDADE SIMULADA" />
            </Campo>
            <Campo rotulo="UF" erro={errors.uf?.message}>
              <input {...register('uf')} className={classeDoInput} maxLength={2} />
            </Campo>
            <Campo rotulo="CEP" erro={errors.cep?.message}>
              <input {...register('cep')} className={classeDoInput} inputMode="numeric" placeholder="30123-456" />
            </Campo>
          </div>
          <div className="mt-3">
            <Campo rotulo="Ponto de referência (opcional)" erro={errors.referencia?.message}>
              <input {...register('referencia')} className={classeDoInput} placeholder="Ex.: ao lado da padaria" />
            </Campo>
          </div>
        </Card>

        <Card>
          <div className="flex items-center justify-between">
            <CardTitle>Equipamentos</CardTitle>
            <Button type="button" variante="fantasma" tamanho="compacto"
              onClick={() => equipamentos.append({ tipo: 'ONT', modelo: '' })}>
              <Plus aria-hidden /> Adicionar
            </Button>
          </div>
          {errors.equipamentos && typeof errors.equipamentos.message === 'string' && (
            <p className="mt-2 text-sm text-erro">{errors.equipamentos.message}</p>
          )}
          <div className="mt-3 flex flex-col gap-3">
            {equipamentos.fields.map((campo, i) => (
              <div key={campo.id} className="rounded-xl border border-borda-forte p-3">
                <div className="grid grid-cols-1 gap-3 md:grid-cols-[1fr_2fr_1fr_1fr_1fr_auto]">
                  <Campo rotulo="Tipo" erro={errors.equipamentos?.[i]?.tipo?.message}>
                    <select {...register(`equipamentos.${i}.tipo` as const)} className={classeDoInput}>
                      <option value="ONT">ONT</option>
                      <option value="ONU">ONU</option>
                      <option value="Roteador">Roteador</option>
                      <option value="RepetidorMesh">Repetidor Mesh</option>
                      <option value="Fonte">Fonte</option>
                    </select>
                  </Campo>
                  <Campo rotulo="Modelo" erro={errors.equipamentos?.[i]?.modelo?.message}>
                    <input {...register(`equipamentos.${i}.modelo` as const)} className={classeDoInput} placeholder="HG8145V5" />
                  </Campo>
                  <Campo rotulo="Série (opcional)" erro={errors.equipamentos?.[i]?.serie?.message}>
                    <input {...register(`equipamentos.${i}.serie` as const)} className={classeDoInput} placeholder="SIMU0001" />
                  </Campo>
                  <Campo rotulo="MAC (opcional)" erro={errors.equipamentos?.[i]?.mac?.message}>
                    <input {...register(`equipamentos.${i}.mac` as const)} className={classeDoInput} placeholder="aa:bb:cc:dd:ee:ff" />
                  </Campo>
                  <Campo rotulo="Valor (R$, opcional)" erro={errors.equipamentos?.[i]?.valor?.message}>
                    <input {...register(`equipamentos.${i}.valor` as const)} className={classeDoInput} inputMode="decimal" placeholder="350,00" />
                  </Campo>
                  <div className="flex items-end">
                    <Button
                      type="button" variante="fantasma" tamanho="compacto"
                      aria-label={`Remover equipamento ${i + 1}`}
                      onClick={() => equipamentos.remove(i)}
                      disabled={equipamentos.fields.length === 1}
                    >
                      <Trash2 aria-hidden />
                    </Button>
                  </div>
                </div>
              </div>
            ))}
          </div>
        </Card>

        <Card>
          <CardTitle>Detalhes da ordem</CardTitle>
          <div className="mt-3 grid grid-cols-1 gap-3 md:grid-cols-[2fr_1fr]">
            <Campo rotulo="Tipo da OS" erro={errors.tipoOs?.message}>
              <input {...register('tipoOs')} className={classeDoInput} />
            </Campo>
            <Campo rotulo="Carência (horas)" erro={errors.carenciaHoras?.message}>
              <input {...register('carenciaHoras')} className={classeDoInput} inputMode="numeric" />
            </Campo>
          </div>
          <div className="mt-3">
            <Campo rotulo="Observações (opcional)" erro={errors.observacao?.message}>
              <textarea
                {...register('observacao')}
                className={`${classeDoInput} min-h-[72px] py-2`}
                placeholder="Ex.: Portão branco. Falar com o porteiro."
              />
            </Campo>
          </div>
        </Card>

        {mensagemDeErro && (
          <p role="alert" className="rounded-xl border border-erro bg-erro/10 p-3 text-sm text-erro">
            {mensagemDeErro}
          </p>
        )}

        <div className="flex gap-3">
          <Button type="submit" disabled={isSubmitting || envio.isPending}>
            {envio.isPending ? 'Cadastrando…' : 'Cadastrar ordem'}
          </Button>
          <Link to="/financeiro/ordens" className="inline-flex min-h-11 items-center px-3 text-azul no-underline">
            Cancelar
          </Link>
        </div>
      </form>
    </div>
  )
}

function Campo({ rotulo, erro, children }: { rotulo: string; erro?: string; children: React.ReactNode }) {
  // O input fica como filho do <label>, sem htmlFor, para o Playwright (e os leitores de tela) acharem
  // o input pelo rótulo: <label><span>Nome</span><input /></label> é o padrão com associação implícita.
  return (
    <label className="flex flex-col gap-1 text-sm">
      <span className="font-semibold text-texto">{rotulo}</span>
      {children}
      {erro && <span className="text-xs text-erro">{erro}</span>}
    </label>
  )
}
