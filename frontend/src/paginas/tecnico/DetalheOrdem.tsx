import { ChevronLeft, Navigation, Phone } from 'lucide-react'
import { Link, useParams } from 'react-router'
import { Carregando, FalhaAoCarregar } from '@/components/EstadoDaTela'
import { Button } from '@/components/ui/button'
import { Card, CardTitle } from '@/components/ui/card'
import { formatarTelefone, janelaDeContato } from '@/features/ordens/contato'
import { descreverEvento, quando } from '@/features/ordens/historico'
import { useOrdem } from '@/features/ordens/consultas'
import { descreverPrazo } from '@/features/ordens/prazo'
import { StatusBadge } from '@/features/ordens/StatusBadge'
import { caixaDeTitulo, enderecoCurto, rotuloDoTipo, type Ordem } from '@/features/ordens/tipos'

export function DetalheOrdem() {
  const { id = '' } = useParams()
  const { data: ordem, isPending, isError, error, refetch } = useOrdem(id)

  return (
    <>
      <header className="flex items-center gap-2 bg-marinho px-3 pt-3 pb-4 text-white">
        <Link to="/tecnico/ordens" aria-label="Voltar para as ordens" className="grid size-12 place-items-center text-white">
          <ChevronLeft aria-hidden className="size-6" />
        </Link>
        <div className="min-w-0 flex-1">
          <h1 className="text-xl font-bold">{ordem ? `OS ${ordem.numeroOs ?? 's/ nº'}` : 'Ordem'}</h1>
          {ordem && <p className="truncate text-[13px] text-texto-claro">{ordem.tipoOs}</p>}
        </div>
        {ordem && <StatusBadge status={ordem.status} />}
      </header>

      <main className="flex flex-1 flex-col gap-3 p-4">
        {isPending && <Carregando linhas={3} rotulo="Carregando a ordem" />}
        {isError && <FalhaAoCarregar erro={error} tentarDeNovo={() => void refetch()} />}
        {ordem && <ConteudoDaOrdem ordem={ordem} />}
      </main>

      {/* Ação principal ao alcance do polegar, acima da barra de navegação (RNF02). */}
      {ordem && (ordem.status === 'atribuida' || ordem.status === 'em_rota') && (
        <div className="sticky bottom-16 flex flex-col gap-2 bg-gradient-to-t from-fundo from-70% px-4 pt-6 pb-3">
          <Button asChild tamanho="grande">
            <Link to={`/tecnico/ordens/${ordem.id}/retirada`}>Iniciar retirada</Link>
          </Button>
          <Button asChild variante="fantasma" tamanho="compacto">
            <Link to={`/tecnico/ordens/${ordem.id}/visita`}>Visita sem sucesso</Link>
          </Button>
        </div>
      )}
    </>
  )
}

function ConteudoDaOrdem({ ordem }: { ordem: Ordem }) {
  const prazo = descreverPrazo(ordem.carenciaAte)
  const { endereco } = ordem
  const destino = ordem.localizacao
    ? `${ordem.localizacao.latitude},${ordem.localizacao.longitude}`
    : `${endereco.logradouro}, ${endereco.numero ?? ''}, ${endereco.cidade} - ${endereco.uf}`

  return (
    <>
      {prazo.vencido && <p className="rounded-[10px] bg-alerta-fundo px-3.5 py-2.5 text-sm font-medium text-alerta">{prazo.texto}</p>}

      <Card>
        <div>
          <p className="text-xs text-texto-suave">Cliente</p>
          <p className="mt-0.5 text-[17px] font-semibold text-marinho">{ordem.cliente}</p>
        </div>
        <div className="flex items-start gap-3">
          <div className="min-w-0 flex-1">
            <p>
              {enderecoCurto(ordem)}
              {endereco.complemento && ` · ${caixaDeTitulo(endereco.complemento)}`}
            </p>
            <p className="mt-0.5 text-[13px] text-texto-suave">
              {caixaDeTitulo(endereco.bairro)} · {caixaDeTitulo(endereco.cidade)}
              {endereco.referencia && ` · ${caixaDeTitulo(endereco.referencia)}`}
            </p>
          </div>
          {/* RF11: abre o app de mapas do celular no endereço. */}
          <a
            href={`https://www.google.com/maps/dir/?api=1&destination=${encodeURIComponent(destino)}`}
            target="_blank"
            rel="noreferrer"
            className="inline-flex min-h-11 shrink-0 items-center gap-1.5 rounded-[10px] border border-azul px-3.5 text-sm font-semibold text-azul no-underline"
          >
            <Navigation aria-hidden className="size-[18px]" />
            Rota
          </a>
        </div>
        {ordem.observacao && <p className="rounded-lg bg-fundo px-2.5 py-2 text-[13px] text-texto-suave">Obs.: {ordem.observacao}</p>}
        <Telefones ordem={ordem} />
      </Card>

      <Card>
        <CardTitle className="text-base sm:text-base">
          Recolher {ordem.equipamentos.length} {ordem.equipamentos.length === 1 ? 'item' : 'itens'}
        </CardTitle>
        <ul className="flex flex-col divide-y divide-borda">
          {ordem.equipamentos.map((e) => (
            <li key={e.id} className="py-2 first:pt-0 last:pb-0">
              <p className="font-semibold">
                {rotuloDoTipo[e.tipo]} · {e.modelo}
              </p>
              <p className="text-[13px] text-texto-suave">{e.numeroSerie ? `SN ${e.numeroSerie}` : 'Sem série'}</p>
            </li>
          ))}
        </ul>
      </Card>

      {ordem.historico && ordem.historico.length > 0 && (
        <Card>
          <CardTitle className="text-base sm:text-base">Histórico</CardTitle>
          <ol className="flex flex-col gap-1.5 text-[13px]">
            {ordem.historico.map((e) => (
              <li key={`${e.tipo}-${e.ocorridoEm}`}>
                <span className="text-texto-suave tabular-nums">{quando(e.ocorridoEm)}</span> · {descreverEvento(e)}
              </li>
            ))}
          </ol>
        </Card>
      )}
    </>
  )
}

/** RN05 e RN10: só oferece ligar para número no padrão e dentro da janela de contato. */
function Telefones({ ordem }: { ordem: Ordem }) {
  const telefones = ordem.telefones ?? []
  if (telefones.length === 0) return <p className="text-[13px] text-texto-suave">Sem telefone cadastrado: o contato fica só pela visita.</p>
  const janela = janelaDeContato()
  return (
    <div data-teste="telefones" className="flex flex-col gap-2">
      <ul className="flex flex-col gap-2">
        {telefones.map((t) => (
          <li key={t.numero} className="flex min-h-11 items-center justify-between gap-3">
            <span className={t.formatoValido ? 'font-medium tabular-nums' : 'text-texto-suave'}>
              {formatarTelefone(t.numero)}
              {!t.formatoValido && ' · fora do padrão'}
            </span>
            {t.formatoValido && janela.permitido && (
              <a
                href={`tel:${t.numero}`}
                className="inline-flex min-h-11 shrink-0 items-center gap-1.5 rounded-[10px] border border-azul px-3.5 text-sm font-semibold text-azul no-underline"
              >
                <Phone aria-hidden className="size-[18px]" />
                Ligar
              </a>
            )}
          </li>
        ))}
      </ul>
      <p className={janela.permitido ? 'text-[13px] text-texto-suave' : 'text-[13px] font-medium text-alerta'}>
        {janela.permitido ? 'Contato de segunda a sábado, das 8h às 18h.' : janela.aviso}
      </p>
    </div>
  )
}
