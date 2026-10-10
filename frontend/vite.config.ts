import { fileURLToPath, URL } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'
import { VitePWA } from 'vite-plugin-pwa'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    react(),
    tailwindcss(),
    VitePWA({
      registerType: 'autoUpdate',
      includeAssets: ['favicon.svg', 'icones/apple-touch-icon.png'],
      manifest: {
        name: 'Retirada Netwise',
        short_name: 'Retirada',
        description: 'Ordens de retirada de equipamentos',
        lang: 'pt-BR',
        start_url: '/',
        display: 'standalone',
        theme_color: '#043E69',
        background_color: '#F5F5F5',
        icons: [
          { src: '/icones/icone-192.png', sizes: '192x192', type: 'image/png' },
          { src: '/icones/icone-512.png', sizes: '512x512', type: 'image/png' },
          { src: '/icones/icone-maskable-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
        ],
      },
      workbox: {
        // A API nunca vem do cache: ordem cancelada precisa aparecer na hora (RN07).
        navigateFallbackDenylist: [/^\/api\//, /^\/health/],
        globPatterns: ['**/*.{js,mjs,css,html,svg,png,woff2,geojson}'],
      },
    }),
  ],
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  build: {
    // O MapLibre sozinho tem ~1 MB e já carrega à parte (import dinâmico).
    chunkSizeWarningLimit: 1100,
  },
  server: {
    // Em desenvolvimento, a API .NET roda em http://localhost:5080.
    proxy: { '/api': 'http://localhost:5080', '/health': 'http://localhost:5080' },
  },
})
