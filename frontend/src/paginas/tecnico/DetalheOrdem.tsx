import { ChevronLeft, Navigation } from 'lucide-react'
import { Link, useParams } from 'react-router'
import { Carregando, FalhaAoCarregar } from '@/components/EstadoDaTela'
import { Card, CardTitle } from '@/components/ui/card'
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
    </>
  )
}
