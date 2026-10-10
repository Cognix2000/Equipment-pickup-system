import { cva, type VariantProps } from 'class-variance-authority'
import type { ComponentProps } from 'react'
import { cn } from '@/lib/utils'

const variantesSelo = cva('inline-flex items-center gap-1.5 rounded-md px-2 py-1 text-xs font-semibold whitespace-nowrap', {
  variants: {
    tom: {
      neutro: 'bg-fundo text-texto-suave',
      info: 'bg-info-fundo text-marinho',
      destaque: 'bg-ciano text-marinho',
      sucesso: 'bg-sucesso-fundo text-sucesso',
      alerta: 'bg-alerta-fundo text-alerta',
      erro: 'bg-erro-fundo text-erro',
    },
  },
  defaultVariants: { tom: 'neutro' },
})

export type TomDoSelo = NonNullable<VariantProps<typeof variantesSelo>['tom']>

export function Badge({ className, tom, ...props }: ComponentProps<'span'> & VariantProps<typeof variantesSelo>) {
  return <span className={cn(variantesSelo({ tom }), className)} {...props} />
}
