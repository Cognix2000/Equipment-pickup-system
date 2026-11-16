import { expect, test, type Page } from '@playwright/test'

/**
 * Chave de acesso (passkey) com o autenticador virtual do Chromium: o navegador cria e assina de verdade,
 * e a API simulada confere se o app mandou a resposta no formato do WebAuthn (base64url).
 * A verificação criptográfica fica nos testes da API (ChaveDeAcessoTestes).
 */

const financeiro = { id: '0192f000-0000-7000-8000-000000000002', nome: 'Financeiro Demonstração', email: 'financeiro.demo@exemplo.invalid', papel: 'financeiro' }
const manipulo = 'AZLwAAAAcACAAAAAAAAAAg' // id do usuário dentro da chave (base64url)
const desafio = () => Buffer.from(crypto.getRandomValues(new Uint8Array(16))).toString('base64url')

async function autenticadorVirtual(page: Page) {
  const cdp = await page.context().newCDPSession(page)
  await cdp.send('WebAuthn.enable')
  await cdp.send('WebAuthn.addVirtualAuthenticator', {
    options: { protocol: 'ctap2', transport: 'internal', hasResidentKey: true, hasUserVerification: true, isUserVerified: true, automaticPresenceSimulation: true },
  })
}

/** API simulada: `logado` diz se a sessão já está completa. */
async function simularApi(page: Page, estado: { logado: boolean }) {
  await page.route('**/api/acesso/eu', (rota) =>
    estado.logado ? rota.fulfill({ json: { ...financeiro, etapa: 'completo', tem2FA: false } }) : rota.fulfill({ status: 401, json: {} }),
  )
  await page.route('**/api/ordens**', (rota) => rota.fulfill({ json: [] }))
}

test('primeiro acesso do financeiro cadastra a digital no lugar do aplicativo, e depois entra só com ela', async ({ page }) => {
  await autenticadorVirtual(page)
  const estado = { logado: false }
  await simularApi(page, estado)
  let credencialCadastrada = ''

  await page.route('**/api/acesso/entrar', (rota) => rota.fulfill({ json: { etapa: 'configurar', usuario: null } }))
  await page.route('**/api/acesso/chave/cadastro/opcoes', (rota) =>
    rota.fulfill({
      json: {
        rp: { id: 'localhost', name: 'Retirada Netwise' },
        user: { id: manipulo, name: financeiro.email, displayName: financeiro.nome },
        challenge: desafio(),
        pubKeyCredParams: [{ type: 'public-key', alg: -7 }],
        timeout: 120000,
        attestation: 'none',
        authenticatorSelection: { residentKey: 'required', requireResidentKey: true, userVerification: 'required' },
        excludeCredentials: [],
        hints: [],
      },
    }),
  )
  await page.route('**/api/acesso/chave/cadastro', async (rota) => {
    const { credencial, nome } = rota.request().postDataJSON()
    expect(rota.request().headers()['x-retirada']).toBe('1')
    expect(credencial.type).toBe('public-key')
    expect(credencial.rawId).toBe(credencial.id)
    expect(credencial.response.attestationObject).toMatch(/^[\w-]+$/)
    expect(credencial.response.clientDataJSON).toMatch(/^[\w-]+$/)
    expect(nome).toBeTruthy()
    credencialCadastrada = credencial.id
    estado.logado = true
    await rota.fulfill({ json: { etapa: 'pronto', usuario: financeiro } })
  })

  await page.goto('/entrar')
  await page.getByLabel('E-mail').fill(financeiro.email)
  await page.getByLabel('Senha', { exact: true }).fill('Demo@Retirada2026')
  await page.getByRole('button', { name: 'Entrar', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Proteja sua conta' })).toBeVisible()
  await page.getByRole('button', { name: 'Cadastrar a digital ou o rosto' }).click()
  await expect(page).toHaveURL(/\/financeiro\/ordens/)
  expect(credencialCadastrada).not.toBe('')

  // Outro dia: entra só com a digital, sem digitar e-mail nem senha.
  estado.logado = false
  await page.route('**/api/acesso/chave/entrar/opcoes', (rota) =>
    rota.fulfill({ json: { challenge: desafio(), timeout: 120000, rpId: 'localhost', allowCredentials: [], userVerification: 'required', hints: [] } }),
  )
  await page.route('**/api/acesso/chave/entrar', async (rota) => {
    const { credencial } = rota.request().postDataJSON()
    expect(credencial.id).toBe(credencialCadastrada)
    expect(credencial.response.userHandle).toBe(manipulo)
    expect(credencial.response.signature).toMatch(/^[\w-]+$/)
    expect(credencial.response.authenticatorData).toMatch(/^[\w-]+$/)
    estado.logado = true
    await rota.fulfill({ json: { etapa: 'pronto', usuario: financeiro } })
  })

  await page.goto('/entrar')
  await page.getByRole('button', { name: 'Entrar com a digital ou o rosto' }).click()
  await expect(page).toHaveURL(/\/financeiro\/ordens/)
})

test('quem prefere continua podendo usar o aplicativo autenticador', async ({ page }) => {
  await simularApi(page, { logado: false })
  await page.route('**/api/acesso/entrar', (rota) => rota.fulfill({ json: { etapa: 'configurar', usuario: null } }))
  await page.route('**/api/acesso/2fa/iniciar', (rota) =>
    rota.fulfill({ json: { enderecoOtpAuth: 'otpauth://totp/Retirada%20Netwise:teste?secret=JBSWY3DPEHPK3PXP', segredo: 'JBSWY3DPEHPK3PXP' } }),
  )

  await page.goto('/entrar')
  await page.getByLabel('E-mail').fill(financeiro.email)
  await page.getByLabel('Senha', { exact: true }).fill('Demo@Retirada2026')
  await page.getByRole('button', { name: 'Entrar', exact: true }).click()
  await page.getByRole('button', { name: 'Usar um aplicativo autenticador' }).click()

  await expect(page.getByText('Não consegue ler o QR code?')).toBeVisible()
  await page.getByRole('button', { name: 'Prefiro usar a digital ou o rosto' }).click()
  await expect(page.getByRole('button', { name: 'Cadastrar a digital ou o rosto' })).toBeVisible()
})

test('com a senha, quem já tem a chave é mandado confirmar com ela', async ({ page }) => {
  await simularApi(page, { logado: false })
  await page.route('**/api/acesso/entrar', (rota) => rota.fulfill({ json: { etapa: 'chave', usuario: null } }))

  await page.goto('/entrar')
  await page.getByLabel('E-mail').fill(financeiro.email)
  await page.getByLabel('Senha', { exact: true }).fill('Demo@Retirada2026')
  await page.getByRole('button', { name: 'Entrar', exact: true }).click()

  await expect(page.getByRole('heading', { name: 'Confirme com a chave de acesso' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Usar a chave de acesso' })).toBeVisible()
  await page.getByRole('button', { name: 'Voltar' }).click()
  await expect(page.getByRole('heading', { name: 'Entrar' })).toBeVisible()
})

test('em Minha conta, remover a chave pede confirmação', async ({ page }) => {
  await simularApi(page, { logado: true })
  let chaves = [{ id: '0192f000-0000-7000-8000-0000000000c1', nome: 'iPhone', criadaEm: '2026-10-20T09:15:00-03:00', ultimoUsoEm: null }]
  await page.route('**/api/acesso/chaves', (rota) => rota.fulfill({ json: chaves }))
  await page.route('**/api/acesso/chaves/*/remover', async (rota) => {
    chaves = []
    await rota.fulfill({ status: 204 })
  })

  await page.goto('/conta')
  await expect(page.getByText('ainda não usada')).toBeVisible()
  await page.getByRole('button', { name: 'Remover a chave iPhone' }).click()
  await page.getByRole('button', { name: 'Manter' }).click()
  await expect(page.getByText('iPhone')).toBeVisible()

  await page.getByRole('button', { name: 'Remover a chave iPhone' }).click()
  await page.getByRole('button', { name: 'Remover de vez' }).click()
  await expect(page.getByText('Nenhuma chave cadastrada ainda.')).toBeVisible()
})
