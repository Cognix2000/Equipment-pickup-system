import type { Meta, StoryObj } from '@storybook/react-vite'
import { StatusBadge } from './StatusBadge'
import { statusOrdem } from './tipos'

const meta = {
  title: 'Base/Selo de situação',
  component: StatusBadge,
  args: { status: 'atribuida' },
  argTypes: { status: { control: 'select', options: statusOrdem.options } },
} satisfies Meta<typeof StatusBadge>

export default meta
type Historia = StoryObj<typeof meta>

export const Uma: Historia = {}

/** Todas as situações lado a lado, para revisar cor e contraste de uma vez. */
export const Todas: Historia = {
  render: () => (
    <div className="flex flex-wrap gap-2">
      {statusOrdem.options.map((s) => (
        <StatusBadge key={s} status={s} />
      ))}
    </div>
  ),
}
