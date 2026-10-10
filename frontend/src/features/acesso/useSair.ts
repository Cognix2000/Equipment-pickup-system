import { useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router'
import { acesso } from './acesso'

/** Sai, limpa o que estava em cache (dados de cliente não ficam no aparelho) e volta ao login. */
export function useSair() {
  const clienteDeConsultas = useQueryClient()
  const navegar = useNavigate()
  return async () => {
    await acesso.sair().catch(() => undefined)
    clienteDeConsultas.clear()
    navegar('/entrar', { replace: true })
  }
}
