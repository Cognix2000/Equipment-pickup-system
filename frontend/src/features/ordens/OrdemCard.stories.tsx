import type { Meta, StoryObj } from '@storybook/react-vite'
import { ordemJoao, ordemMaria } from './exemplos'
import { OrdemCard } from './OrdemCard'

const meta = {
  title: 'Técnico/Cartão de ordem',
  component: OrdemCard,
  args: { ordem: ordemMaria, posicao: 1 },
} satisfies Meta<typeof OrdemCard>

export default meta
type Historia = StoryObj<typeof meta>

/** A primeira da lista: destaque, prazo e botão de abrir. */
export const Sugerida: Historia = { args: { sugerida: true } }

/** As demais: uma linha, toque abre o detalhe. */
export const Compacta: Historia = { args: { ordem: ordemJoao, posicao: 2 } }
