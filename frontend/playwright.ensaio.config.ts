import { defineConfig, devices } from '@playwright/test'

/**
 * Ensaio da demonstração: percorre o fluxo inteiro contra um ambiente DE VERDADE (API, banco e fotos),
 * com os usuários fictícios. Muda dados: rode só em homologação, nunca em produção.
 *
 *   ENSAIO_URL=http://localhost:8080 npm run ensaio
 *
 * As capturas de cada passo ficam em ensaio-resultado/.
 */
export default defineConfig({
  testDir: './ensaio',
  workers: 1,
  retries: 0,
  timeout: 120_000, // o 2FA pode esperar o próximo código de 30 s
  reporter: 'list',
  outputDir: 'ensaio-resultado/rastros',
  use: {
    baseURL: process.env.ENSAIO_URL ?? 'http://localhost:8080',
    locale: 'pt-BR',
    timezoneId: 'America/Sao_Paulo',
    reducedMotion: 'reduce',
    serviceWorkers: 'block',
    launchOptions: { executablePath: process.env.CHROMIUM_PATH || undefined },
  },
  projects: [{ name: 'ensaio', use: { ...devices['Desktop Chrome'], viewport: { width: 1280, height: 800 } } }],
})
