import { useEffect, useRef, useState } from 'react'
import { novaChave, type FotoDaRetirada } from './comandos'
import { enviarFoto, prepararFoto } from './fotos'

/** Mesmo limite do servidor (Ordem.LimiteDeFotos). */
export const LIMITE_DE_FOTOS = 10

export type FotoNaTela = {
  local: string
  tipo: FotoDaRetirada['tipo']
  equipamentoId: string | null
  url: string
  capturadaEm: string
  estado: 'enviando' | 'enviada' | 'falhou'
  id?: string
  arquivo: Blob
}

/**
 * RN14: as fotos da retirada. Cada uma é preparada no aparelho (menor e sem EXIF) e enviada assim que é tirada;
 * a retirada só leva os ids. Se o sinal cair, a foto fica na tela com "Tentar de novo".
 */
export function useFotosDaRetirada(ordemId: string) {
  const [fotos, setFotos] = useState<FotoNaTela[]>([])
  const [erro, setErro] = useState<string | null>(null)

  // As miniaturas são URLs locais do navegador: liberadas quando a tela fecha.
  const urls = useRef<string[]>([])
  useEffect(() => () => urls.current.forEach((u) => URL.revokeObjectURL(u)), [])

  const mudar = (local: string, parte: Partial<FotoNaTela>) => setFotos((lista) => lista.map((f) => (f.local === local ? { ...f, ...parte } : f)))

  const enviar = async (local: string, arquivo: Blob) => {
    mudar(local, { estado: 'enviando' })
    try {
      const enviada = await enviarFoto(ordemId, arquivo)
      mudar(local, { estado: 'enviada', id: enviada.id })
    } catch {
      mudar(local, { estado: 'falhou' })
    }
  }

  const adicionar = async (original: File, tipo: FotoNaTela['tipo'], equipamentoId: string | null) => {
    setErro(null)
    let arquivo: Blob
    try {
      arquivo = await prepararFoto(original)
    } catch {
      setErro('Não foi possível abrir essa foto. Tire de novo.')
      return
    }
    const url = URL.createObjectURL(arquivo)
    urls.current.push(url)
    const local = novaChave()
    const nova: FotoNaTela = { local, tipo, equipamentoId, url, capturadaEm: new Date().toISOString(), estado: 'enviando', arquivo }
    // A etiqueta é uma por item: a nova substitui a anterior.
    setFotos((lista) => [...lista.filter((f) => !(tipo === 'etiqueta_serie' && f.tipo === tipo && f.equipamentoId === equipamentoId)), nova])
    await enviar(local, arquivo)
  }

  const remover = (local: string) => setFotos((lista) => lista.filter((f) => f.local !== local))
  const tentarDeNovo = (foto: FotoNaTela) => void enviar(foto.local, foto.arquivo)

  const paraEnviar: FotoDaRetirada[] = fotos
    .filter((f) => f.estado === 'enviada' && f.id)
    .map((f) => ({ id: f.id!, tipo: f.tipo, equipamentoId: f.equipamentoId, capturadaEm: f.capturadaEm }))

  return { fotos, erro, adicionar, remover, tentarDeNovo, paraEnviar, enviando: fotos.some((f) => f.estado === 'enviando') }
}
