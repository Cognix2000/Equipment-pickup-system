import { Eye, EyeOff } from 'lucide-react'
import { useState, type ComponentProps } from 'react'
import { Button } from '@/components/ui/button'
import { classeDoInput } from '@/components/ui/input'
import { cn } from '@/lib/utils'

/** Campo de senha com o botão de olho para conferir o que foi digitado. Começa sempre oculto. */
export function CampoDeSenha({ className, ...props }: Omit<ComponentProps<'input'>, 'type'>) {
  const [visivel, setVisivel] = useState(false)
  return (
    <div className="relative">
      <input type={visivel ? 'text' : 'password'} className={cn(classeDoInput, 'pr-12', className)} {...props} />
      <Button
        type="button"
        variante="fantasma"
        tamanho="icone"
        className="absolute inset-y-0 right-0 text-texto-suave"
        aria-label={visivel ? 'Ocultar senha' : 'Mostrar senha'}
        aria-pressed={visivel}
        onClick={() => setVisivel((v) => !v)}
      >
        {visivel ? <EyeOff aria-hidden /> : <Eye aria-hidden />}
      </Button>
    </div>
  )
}
