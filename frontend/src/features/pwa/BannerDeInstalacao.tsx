/**
 * Banner de convite para instalar o PWA.
 *
 * - Android/Chrome: botão "Instalar" chama o prompt nativo do navegador.
 * - iOS/Safari:     mostra tutorial "Toque em compartilhar → Adicionar à Tela de Início".
 * - O "✕" dispensa permanentemente (localStorage).
 */

import { Download, Share, X } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { useInstalacaoPwa } from './useInstalacaoPwa'

export function BannerDeInstalacao() {
  const { podeMostrar, ehIos, instalar, dispensar } = useInstalacaoPwa()

  if (!podeMostrar && !ehIos) return null

  return (
    <div
      role="banner"
      aria-label="Instalar aplicativo"
      className="flex items-center gap-3 border-b border-borda bg-superficie px-4 py-3 text-sm"
    >
      {/* Ícone */}
      <span className="shrink-0 rounded-lg bg-primario/10 p-2 text-primario" aria-hidden>
        {ehIos ? <Share className="size-5" /> : <Download className="size-5" />}
      </span>

      {/* Texto */}
      <div className="min-w-0 flex-1">
        <p className="font-medium text-texto">Instalar o aplicativo</p>
        {ehIos ? (
          <p className="text-texto-suave">
            Toque em <strong>Compartilhar</strong> e depois em{' '}
            <strong>Adicionar à Tela de Início</strong>.
          </p>
        ) : (
          <p className="text-texto-suave">Acesse mais rápido, mesmo sem internet.</p>
        )}
      </div>

      {/* Ação Android */}
      {!ehIos && (
        <Button
          type="button"
          tamanho="compacto"
          onClick={() => void instalar()}
          className="shrink-0"
        >
          Instalar
        </Button>
      )}

      {/* Dispensar */}
      <button
        type="button"
        onClick={dispensar}
        aria-label="Dispensar convite de instalação"
        className="shrink-0 rounded p-1 text-texto-suave hover:bg-fundo motion-reduce:transition-none"
      >
        <X className="size-4" aria-hidden />
      </button>
    </div>
  )
}
