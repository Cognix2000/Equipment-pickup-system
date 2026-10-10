import type { ComponentProps } from 'react'
import { cn } from '@/lib/utils'

export function Card({ className, ...props }: ComponentProps<'section'>) {
  return <section className={cn('flex flex-col gap-3 rounded-xl bg-superficie p-4 sm:p-5', className)} {...props} />
}

export function CardTitle({ className, ...props }: ComponentProps<'h2'>) {
  return <h2 className={cn('font-titulo text-lg font-bold text-marinho sm:text-xl', className)} {...props} />
}
