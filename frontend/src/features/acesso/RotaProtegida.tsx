import type { ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router'
import { Carregando, FalhaAoCarregar } from '@/components/EstadoDaTela'
import { inicioDo, useEu, type Papel } from './acesso'

/**
 * Só mostra a tela para quem entrou com o papel certo. A regra de verdade está no servidor (RNF04):
 * aqui é só para não mostrar uma tela que a API vai recusar.
 */
export function RotaProtegida({ papeis, children }: { papeis: Papel[]; children: ReactNode }) {
  const { data: eu, isPending, isError, error, refetch } = useEu()
  const local = useLocation()

  if (isPending) {
    return (
      <div className="mx-auto max-w-lg p-6">
        <Carregando linhas={2} rotulo="Conferindo o acesso" />
      </div>
    )
  }
  if (isError) {
    return (
      <div className="mx-auto max-w-lg p-6">
        <FalhaAoCarregar erro={error} tentarDeNovo={() => void refetch()} />
      </div>
    )
  }
  if (!eu || eu.etapa !== 'completo') {
    return <Navigate to={`/entrar?volta=${encodeURIComponent(local.pathname)}`} replace />
  }
  if (!papeis.includes(eu.papel)) return <Navigate to={inicioDo(eu.papel)} replace />
  return children
}

/** Raiz do app: manda cada um para a sua tela, ou para o login. */
export function Inicio() {
  const { data: eu, isPending } = useEu()
  if (isPending) return null
  if (!eu || eu.etapa !== 'completo') return <Navigate to="/entrar" replace />
  return <Navigate to={inicioDo(eu.papel)} replace />
}
