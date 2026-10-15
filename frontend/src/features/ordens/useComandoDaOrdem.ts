import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { ErroDaApi } from '@/lib/api'
import { novaChave } from './comandos'
import { ordemQuery } from './consultas'
import type { Ordem } from './tipos'

/**
 * Um comando da ordem na tela (ADR-009). A chave de idempotência nasce com o componente e é reaproveitada
 * se a pessoa tentar de novo depois de uma falha de rede: o servidor não repete o que já fez.
 * Com sucesso, a tela passa a mostrar a ordem que o servidor devolveu e as listas são recarregadas.
 *
 * `aoConcluir` fica aqui, e não no `mutate`: a ordem nova costuma tirar da tela o botão que disparou
 * o comando, e o retorno do `mutate` não roda em componente que já saiu da tela.
 */
export function useComandoDaOrdem<T = void>(
  ordemId: string,
  executar: (dados: T, chave: string) => Promise<Ordem>,
  aoConcluir?: (ordem: Ordem) => void,
) {
  const clienteDeConsultas = useQueryClient()
  const [chave] = useState(novaChave)
  return useMutation({
    mutationFn: (dados: T) => executar(dados, chave),
    onSuccess: async (atualizada) => {
      aoConcluir?.(atualizada)
      clienteDeConsultas.setQueryData(ordemQuery(ordemId).queryKey, atualizada)
      await clienteDeConsultas.invalidateQueries({ queryKey: ['ordens'] })
    },
  })
}

/** Texto da falha para mostrar perto do botão. */
export function mensagemDaFalha(falha: Error | null) {
  if (!falha) return null
  return falha instanceof ErroDaApi ? falha.message : 'Sem resposta do servidor. Confira a conexão e tente de novo.'
}
