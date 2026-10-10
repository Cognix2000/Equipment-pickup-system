import { Slot } from '@radix-ui/react-slot'
import { cva, type VariantProps } from 'class-variance-authority'
import type { ComponentProps } from 'react'
import { cn } from '@/lib/utils'

// Componente no padrão shadcn/ui: o código fica no projeto e segue os tokens da Netwise.
const variantesBotao = cva(
  'inline-flex items-center justify-center gap-2 rounded-[10px] font-texto font-semibold whitespace-nowrap transition-colors disabled:pointer-events-none disabled:bg-desabilitado disabled:text-[#3b4652] disabled:border-transparent [&_svg]:size-5 [&_svg]:shrink-0',
  {
    variants: {
      variante: {
        principal: 'bg-azul text-white hover:bg-marinho',
        contorno: 'border border-azul bg-superficie text-azul hover:bg-info-fundo',
        neutro: 'border border-borda-forte bg-superficie text-texto hover:bg-fundo',
        perigo: 'border border-erro bg-superficie text-erro hover:bg-erro-fundo',
        fantasma: 'text-azul hover:bg-info-fundo',
      },
      tamanho: {
        // Alvos de toque de pelo menos 44–48 px (RNF02).
        padrao: 'min-h-12 px-5 text-base',
        compacto: 'min-h-11 px-4 text-[15px]',
        grande: 'min-h-13 rounded-xl px-6 text-[17px]',
        icone: 'size-12',
      },
    },
    defaultVariants: { variante: 'principal', tamanho: 'padrao' },
  },
)

type BotaoProps = ComponentProps<'button'> &
  VariantProps<typeof variantesBotao> & {
    /** Renderiza o filho (ex.: um link) com a aparência de botão. */
    asChild?: boolean
  }

export function Button({ className, variante, tamanho, asChild = false, ...props }: BotaoProps) {
  const Componente = asChild ? Slot : 'button'
  return <Componente className={cn(variantesBotao({ variante, tamanho }), className)} {...props} />
}
