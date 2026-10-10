import { Badge } from '@/components/ui/badge'
import { rotuloDoStatus, type StatusOrdem } from './tipos'

export function StatusBadge({ status }: { status: StatusOrdem }) {
  const { texto, tom } = rotuloDoStatus[status]
  return <Badge tom={tom}>{texto}</Badge>
}
