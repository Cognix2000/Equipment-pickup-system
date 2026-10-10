import { defineConfig, devices } from '@playwright/test'

/**
 * Testes de tela: acessibilidade (axe), nada de rolagem lateral e comparação de capturas
 * nas três larguras dos protótipos. A API é simulada com as ordens fictícias.
 *
 * Capturas de referência: gere e versione com o mesmo navegador do CI
 * (`npm run test:e2e -- --update-snapshots` dentro da imagem oficial do Playwright).
 */
export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : 'list',
  // Sem captura de referência ainda: cria e passa; com captura, compara.
  updateSnapshots: 'missing',
  expect: { toHaveScreenshot: { maxDiffPixelRatio: 0.01, animations: 'disabled' } },
  use: {
    baseURL: 'http://localhost:4174',
    locale: 'pt-BR',
    timezoneId: 'America/Sao_Paulo',
    // O service worker do PWA não pode atender no lugar da API simulada.
    serviceWorkers: 'block',
    launchOptions: {
      executablePath: process.env.CHROMIUM_PATH || undefined,
      args: ['--use-gl=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'],
    },
  },
  projects: [
    { name: 'celular', use: { ...devices['Desktop Chrome'], viewport: { width: 390, height: 844 } } },
    { name: 'tablet', use: { ...devices['Desktop Chrome'], viewport: { width: 820, height: 1180 } } },
    { name: 'desktop', use: { ...devices['Desktop Chrome'], viewport: { width: 1280, height: 800 } } },
  ],
  webServer: {
    command: 'npm run build && npx vite preview --port 4174 --strictPort',
    url: 'http://localhost:4174',
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
})
