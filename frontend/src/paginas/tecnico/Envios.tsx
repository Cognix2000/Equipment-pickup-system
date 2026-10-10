import { Vazio } from '@/components/EstadoDaTela'

/** Fila de envios do modo offline (incremento 2). Por enquanto, sempre vazia. */
export function Envios() {
  return (
    <>
      <header className="bg-marinho px-5 pt-5 pb-4 text-white">
        <h1 className="text-[22px] font-bold">Envios</h1>
      </header>
      <main className="flex-1 p-4">
        <Vazio titulo="Tudo enviado">Retiradas registradas sem sinal ficam aqui até o celular voltar a ter rede.</Vazio>
      </main>
    </>
  )
}
