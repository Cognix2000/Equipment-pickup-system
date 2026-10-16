import { createHmac } from 'node:crypto'
import path from 'node:path'
import { expect, test, type Browser, type Page } from '@playwright/test'

/**
 * O roteiro de docs/roteiro-de-demonstracao.md, de ponta a ponta, contra a API de verdade.
 * Cada passo entra com um usuário fictício diferente, como na apresentação.
 */
test.describe.configure({ mode: 'serial' })

const SENHA = 'Demo@Retirada2026'
const RAIZ = path.join(import.meta.dirname, '..', '..')
// Formato da exportação do HubSoft: uma linha por equipamento, então as ordens já chegam com os itens.
const PLANILHA = path.join(RAIZ, 'ferramentas', 'dados-ficticios', 'modelo_exportacao_financeiro.xlsx')
const FOTO = path.join(import.meta.dirname, '..', 'e2e', 'arquivos', 'foto-ficticia.jpg')
const CAPTURAS = path.join(import.meta.dirname, '..', 'ensaio-resultado')

/** Segredos do 2FA de cada pessoa, guardados quando ela configura no primeiro login. */
const segredos: Record<string, string> = {}
let numeroDaOs = ''
let ordemId = ''

/** Último passo de 30 s usado por pessoa: o servidor não aceita o mesmo código duas vezes. */
const ultimoPasso: Record<string, number> = {}
const passoAtual = () => Math.floor(Date.now() / 30_000)

/** Espera o próximo código, se o atual já foi usado, e devolve o código do autenticador. */
async function codigoNovo(email: string) {
  while (passoAtual() <= (ultimoPasso[email] ?? -1)) await new Promise((r) => setTimeout(r, 1_000))
  ultimoPasso[email] = passoAtual()
  return codigoDoAutenticador(segredos[email])
}

/** TOTP (RFC 6238) com SHA-1, 30 s e 6 dígitos, como o aplicativo autenticador. */
function codigoDoAutenticador(segredo: string) {
  const alfabeto = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
  let bits = ''
  for (const c of segredo.replace(/=+$/, '').toUpperCase()) bits += alfabeto.indexOf(c).toString(2).padStart(5, '0')
  const chave = Buffer.from(bits.match(/.{8}/g)!.map((b) => parseInt(b, 2)))
  const contador = Buffer.alloc(8)
  contador.writeBigUInt64BE(BigInt(Math.floor(Date.now() / 30_000)))
  const hmac = createHmac('sha1', chave).update(contador).digest()
  const i = hmac[hmac.length - 1] & 0xf
  return String((hmac.readUInt32BE(i) & 0x7fffffff) % 1_000_000).padStart(6, '0')
}

async function entrar(browser: Browser, email: string, opcoes: Parameters<Browser['newContext']>[0] = {}): Promise<Page> {
  const contexto = await browser.newContext(opcoes)
  const page = await contexto.newPage()
  await page.goto('/entrar')
  await page.getByLabel('E-mail').fill(email)
  await page.getByLabel('Senha').fill(SENHA)
  await page.getByRole('button', { name: 'Entrar' }).click()

  const configurar = page.getByRole('heading', { name: 'Proteja sua conta' })
  const codigo = page.getByRole('heading', { name: 'Código de verificação' })
  const etapa = await Promise.race([
    configurar.waitFor().then(() => 'configurar' as const),
    codigo.waitFor().then(() => 'codigo' as const),
    page.waitForURL((u) => !u.pathname.startsWith('/entrar')).then(() => 'dentro' as const),
  ])
  if (etapa === 'configurar') {
    await page.getByText('Não consegue ler o QR code?').click()
    segredos[email] = (await page.locator('details p').last().innerText()).replace(/\s/g, '')
    await page.getByLabel('Código de 6 números').fill(await codigoNovo(email))
    await page.getByRole('button', { name: 'Ativar e entrar' }).click()
  } else if (etapa === 'codigo') {
    if (!segredos[email]) throw new Error(`${email} já tem 2FA: rode o ensaio num banco recém-carregado.`)
    await page.getByLabel('Código de 6 números').fill(await codigoNovo(email))
    await page.getByRole('button', { name: 'Confirmar' }).click()
  }
  await page.waitForURL((u) => !u.pathname.startsWith('/entrar'))
  return page
}

const capturar = (page: Page, nome: string) => page.screenshot({ path: path.join(CAPTURAS, `${nome}.png`), fullPage: true })

test('1. financeiro importa a planilha e confirma', async ({ browser }) => {
  const page = await entrar(browser, 'financeiro.demo@exemplo.invalid')
  await page.goto('/financeiro/importar')
  await page.locator('input[type="file"]').setInputFiles(PLANILHA)
  await page.getByRole('button', { name: 'Ver a prévia' }).click()
  const gravar = page.getByRole('button', { name: /^Gravar \d+ orde(m|ns)$/ })
  await expect(gravar).toBeVisible({ timeout: 20_000 })
  await capturar(page, '01-previa-da-importacao')
  await gravar.click()
  await expect(page.getByText(/gravad|importad/i).first()).toBeVisible({ timeout: 20_000 })
  await capturar(page, '02-importacao-confirmada')
})

test('2. financeiro abre uma ordem em carência, libera e registra o WhatsApp', async ({ browser }) => {
  const page = await entrar(browser, 'financeiro.demo@exemplo.invalid')
  await page.goto('/financeiro/ordens')
  await page.getByRole('button', { name: /^Em carência/ }).click()
  const primeira = page.locator('tbody tr').first().getByRole('link')
  numeroDaOs = (await primeira.innerText()).trim()
  await primeira.click()
  await expect(page.getByRole('heading', { name: `OS ${numeroDaOs}` })).toBeVisible()
  ordemId = new URL(page.url()).pathname.split('/').pop()!
  await page.getByRole('button', { name: 'Liberar agora' }).click()
  await expect(page.getByRole('status')).toContainText('Ordem liberada')

  const whatsapp = page.getByRole('button', { name: 'Registrar WhatsApp' }).first()
  if (await whatsapp.count()) {
    await whatsapp.click()
    await page.getByRole('button', { name: 'Enviada' }).click()
    await expect(page.getByText('Contato registrado no histórico.')).toBeVisible()
  }
  await capturar(page, '03-ordem-liberada-no-painel')
})

test('3. gestor designa a ordem ao técnico', async ({ browser }) => {
  const page = await entrar(browser, 'gestor.demo@exemplo.invalid')
  await page.goto('/financeiro/ordens')
  await page.locator('tbody').getByRole('link', { name: numeroDaOs, exact: true }).click()
  await page.getByLabel('Técnico').selectOption({ label: 'Técnico Demonstração' })
  await page.getByRole('button', { name: 'Designar' }).click()
  await expect(page.getByRole('status')).toContainText('Ordem designada')
  await capturar(page, '04-ordem-designada')
})

test('4. técnico registra a retirada com série, foto e GPS', async ({ browser }) => {
  const page = await entrar(browser, 'tecnico.demo@exemplo.invalid', {
    viewport: { width: 390, height: 844 },
    permissions: ['geolocation'],
    geolocation: { latitude: -21.0012, longitude: -38.9978, accuracy: 8 },
  })
  await capturar(page, '05-tecnico-minhas-ordens')
  // Na apresentação, o técnico toca na ordem que acabou de receber; aqui o ensaio vai direto a ela.
  await page.goto(`/tecnico/ordens/${ordemId}`)
  await expect(page.getByRole('heading', { name: `OS ${numeroDaOs}` })).toBeVisible()
  await capturar(page, '05-tecnico-detalhe-da-ordem')
  await page.getByRole('link', { name: 'Iniciar retirada' }).click()

  // Digita o SN que está na etiqueta (o esperado aparece na tela).
  await expect(page.getByLabel(/SN da etiqueta/).first()).toBeVisible()
  for (const campo of await page.getByLabel(/SN da etiqueta/).all()) {
    const rotulo = await page.locator(`label[for="${await campo.getAttribute('id')}"]`).innerText()
    await campo.fill(/esperado: ([^)]+)\)/.exec(rotulo)![1])
  }
  for (const recolhida of await page.getByLabel('Recolhida').all()) await recolhida.check()
  await page.getByLabel('Tirar foto dos equipamentos').setInputFiles(FOTO)
  await expect(page.getByText('Enviada')).toBeVisible({ timeout: 20_000 })
  await capturar(page, '06-tecnico-retirada')
  await page.getByRole('button', { name: 'Confirmar retirada', exact: true }).click()
  await expect(page.getByText('Retirada registrada.')).toBeVisible()
})

test('5. estoque confere e fecha a ordem', async ({ browser }) => {
  const page = await entrar(browser, 'estoque.demo@exemplo.invalid')
  await page.getByLabel('Protocolo, número da OS ou SN de um item').fill(numeroDaOs)
  await page.getByRole('button', { name: 'Buscar' }).click()
  await expect(page.getByLabel(/SN lido/).first()).toBeVisible() // espera a ordem abrir
  for (const campo of await page.getByLabel(/SN lido/).all()) {
    const rotulo = await page.locator(`label[for="${await campo.getAttribute('id')}"]`).innerText()
    await campo.fill(/esperado: ([^)]+)\)/.exec(rotulo)![1])
  }
  await capturar(page, '07-estoque-lendo-as-series')
  await page.getByRole('button', { name: 'Fechar como Conferida' }).click()
  await expect(page.getByText('Ordem conferida.', { exact: false })).toBeVisible()
  await capturar(page, '08-estoque-conferida')
})

test('6. financeiro vê as fotos e marca a baixa no HubSoft', async ({ browser }) => {
  const page = await entrar(browser, 'financeiro.demo@exemplo.invalid')
  await page.goto('/financeiro/ordens')
  await page.getByRole('button', { name: /^Para dar baixa/ }).click()
  await page.locator('tbody').getByRole('link', { name: numeroDaOs, exact: true }).click()
  const foto = page.getByRole('heading', { name: 'Fotos da retirada' }).locator('..').getByRole('img').first()
  await expect.poll(() => foto.evaluate((img: HTMLImageElement) => img.naturalWidth)).toBeGreaterThan(0)
  await page.getByRole('button', { name: 'Marcar baixa feita' }).click()
  await expect(page.getByRole('status')).toContainText('Baixa marcada')
  await capturar(page, '09-financeiro-baixa-e-fotos')
})
