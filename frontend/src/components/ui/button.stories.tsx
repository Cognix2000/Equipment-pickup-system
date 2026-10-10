import type { Meta, StoryObj } from '@storybook/react-vite'
import { Camera } from 'lucide-react'
import { Button } from './button'

const meta = {
  title: 'Base/Botão',
  component: Button,
  args: { children: 'Iniciar retirada' },
} satisfies Meta<typeof Button>

export default meta
type Historia = StoryObj<typeof meta>

export const Principal: Historia = {}
export const Contorno: Historia = { args: { variante: 'contorno', children: 'Rota' } }
export const Neutro: Historia = { args: { variante: 'neutro', children: 'Trocar arquivo' } }
export const Perigo: Historia = { args: { variante: 'perigo', children: 'Cancelar ordem' } }
export const Desabilitado: Historia = { args: { disabled: true, tamanho: 'grande', children: 'Confirmar retirada · falta 1 item' } }
export const ComIcone: Historia = {
  args: {
    variante: 'contorno',
    children: (
      <>
        <Camera aria-hidden /> Etiqueta
      </>
    ),
  },
}
