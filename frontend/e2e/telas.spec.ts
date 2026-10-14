import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page } from '@playwright/test'
import { ordemMaria, todasAsOrdens } from '../src/features/ordens/exemplos'

type Papel = 'tecnico' | 'financeiro' | 'admin' | null

const usuarios = [
  { id: '0192f000-0000-7000-8000-000000000004', nome: 'Gestor Demonstração', email: 'gestor.demo@exemplo.invalid', papel: 'admin', situacao: 'ativo', doisFatores: true, ultimoLoginEm: '2026-10-08T12:00:00-03:00' },
  { id: '0192f000-0000-7000-8000-000000000002', nome: 'Financeiro Demonstração', email: 'financeiro.demo@exemplo.invalid', papel: 'financeiro', situacao: 'ativo', doisFatores: false, ultimoLoginEm: null },
  { id: '0192f000-0000-7000-8000-000000000001', nome: 'Técnico Demonstração', email: 'tecnico.demo@exemplo.invalid', papel: 'tecnico', situacao: 'pendente', doisFatores: false, ultimoLoginEm: null },
  { id: '0192f000-0000-7000-8000-000000000005', nome: 'Técnico Desligado', email: 'desligado@exemplo.invalid', papel: 'tecnico', situacao: 'revogado', doisFatores: false, ultimoLoginEm: '2026-09-30T08:00:00-03:00' },
]

/** Simula a API com dados fictícios. `papel` = quem está logado (null = ninguém). */
async function simularApi(page: Page, papel: Papel) {
  await page.route('**/api/acesso/eu', (rota) =>
    papel
      ? rota.fulfill({ json: { id: '0192f000-0000-7000-8000-000000000009', nome: 'Pessoa de Teste', papel, etapa: 'completo' } })
      : rota.fulfill({ status: 401, json: { title: 'Não autenticado' } }),
  )
  await page.route('**/api/usuarios', (rota) => rota.fulfill({ json: usuarios }))
  await page.route('**/api/ordens**', async (rota) => {
    const url = new URL(rota.request().url())
    const id = url.pathname.split('/api/ordens/')[1]
    if (id) {
      const ordem = todasAsOrdens.find((o) => o.id === id)
      return ordem ? rota.fulfill({ json: ordem }) : rota.fulfill({ status: 404, json: { title: 'Ordem não encontrada.' } })
    }
    const status = url.searchParams.get('status')
    return rota.fulfill({ json: status ? todasAsOrdens.filter((o) => o.status === status) : todasAsOrdens })
  })
}

const telas: { nome: string; rota: string; pronto: string; papel: Papel }[] = [
  { nome: 'entrar', rota: '/entrar', pronto: 'Esqueceu a senha?', papel: null },
  { nome: 'primeiro-acesso', rota: '/primeiro-acesso#t=token-ficticio', pronto: 'Crie sua senha', papel: null },
  { nome: 'minhas-ordens', rota: '/tecnico/ordens', pronto: 'Ver ordem', papel: 'tecnico' },
  { nome: 'detalhe-da-ordem', rota: `/tecnico/ordens/${ordemMaria.id}`, pronto: 'Recolher 3 itens', papel: 'tecnico' },
  { nome: 'ordens-do-financeiro', rota: '/financeiro/ordens', pronto: 'MARIA EXEMPLO SILVA', papel: 'financeiro' },
  { nome: 'importar-planilha', rota: '/financeiro/importar', pronto: 'Escolher arquivo', papel: 'financeiro' },
  { nome: 'usuarios', rota: '/gestao/usuarios', pronto: 'Cadastrar e gerar convite', papel: 'admin' },
]

for (const tela of telas) {
  test.describe(tela.nome, () => {
    test.beforeEach(async ({ page }) => {
      await simularApi(page, tela.papel)
      await page.goto(tela.rota)
      await expect(page.getByText(tela.pronto).filter({ visible: true }).first()).toBeVisible()
    })

    test('sem problemas de acessibilidade (WCAG AA)', async ({ page }) => {
      const resultado = await new AxeBuilder({ page })
        .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
        // O canvas do mapa é desenhado pelo MapLibre; os pinos são botões e entram na checagem.
        .exclude('.maplibregl-canvas-container')
        .analyze()
      expect(resultado.violations.map((v) => `${v.id}: ${v.help}`)).toEqual([])
    })

    test('não rola de lado', async ({ page }) => {
      const larguraDaPagina = await page.evaluate(() => document.documentElement.scrollWidth)
      expect(larguraDaPagina).toBeLessThanOrEqual(page.viewportSize()!.width)
    })

    test('igual à captura de referência', async ({ page }) => {
      await expect(page).toHaveScreenshot(`${tela.nome}.png`, {
        fullPage: true,
        // O mapa depende da GPU; a comparação fica para o resto da tela.
        mask: [page.locator('.maplibregl-map')],
      })
    })
  })
}

test('sem login, a tela protegida manda para o login e volta depois', async ({ page }) => {
  await simularApi(page, null)
  await page.goto('/tecnico/ordens')
  await expect(page).toHaveURL(/\/entrar\?volta=%2Ftecnico%2Fordens/)
})

test('técnico não abre a gestão de usuários', async ({ page }) => {
  await simularApi(page, 'tecnico')
  await page.goto('/gestao/usuarios')
  await expect(page).toHaveURL(/\/tecnico\/ordens/)
})

test('login de técnico vai direto para as ordens', async ({ page }) => {
  let papel: Papel = null
  await simularApi(page, null)
  await page.route('**/api/acesso/eu', (rota) =>
    papel
      ? rota.fulfill({ json: { id: '0192f000-0000-7000-8000-000000000001', nome: 'Técnico Demonstração', papel, etapa: 'completo' } })
      : rota.fulfill({ status: 401, json: {} }),
  )
  await page.route('**/api/acesso/entrar', async (rota) => {
    expect(rota.request().headers()['x-retirada']).toBe('1') // cabeçalho anti-CSRF (ADR-010)
    papel = 'tecnico'
    await rota.fulfill({
      json: { etapa: 'pronto', usuario: { id: '0192f000-0000-7000-8000-000000000001', nome: 'Técnico Demonstração', email: 'tecnico.demo@exemplo.invalid', papel: 'tecnico' } },
    })
  })

  await page.goto('/entrar')
  await page.getByLabel('E-mail').fill('tecnico.demo@exemplo.invalid')
  await page.getByLabel('Senha').fill('Demo@Retirada2026')
  await page.getByRole('button', { name: 'Entrar' }).click()

  await expect(page.getByRole('heading', { name: 'Minhas ordens' })).toBeVisible()
})

test('login de financeiro pede o código do aplicativo', async ({ page }) => {
  await simularApi(page, null)
  await page.route('**/api/acesso/entrar', (rota) => rota.fulfill({ json: { etapa: 'codigo', usuario: null } }))

  await page.goto('/entrar')
  await page.getByLabel('E-mail').fill('financeiro.demo@exemplo.invalid')
  await page.getByLabel('Senha').fill('Demo@Retirada2026')
  await page.getByRole('button', { name: 'Entrar' }).click()

  await expect(page.getByRole('heading', { name: 'Código de verificação' })).toBeVisible()
  await page.getByRole('button', { name: 'Confirmar' }).click()
  await expect(page.getByText('O código tem 6 números.')).toBeVisible()
})

test('senha errada mostra o aviso da API', async ({ page }) => {
  await simularApi(page, null)
  await page.route('**/api/acesso/entrar', (rota) => rota.fulfill({ status: 401, json: { title: 'E-mail ou senha incorretos.' } }))

  await page.goto('/entrar')
  await page.getByLabel('E-mail').fill('alguem@exemplo.invalid')
  await page.getByLabel('Senha').fill('errada')
  await page.getByRole('button', { name: 'Entrar' }).click()

  await expect(page.getByRole('alert')).toHaveText('E-mail ou senha incorretos.')
})

test('ordem sugerida abre o detalhe', async ({ page }) => {
  await simularApi(page, 'tecnico')
  await page.goto('/tecnico/ordens')
  await page.getByRole('link', { name: 'Ver ordem' }).click()
  await expect(page.getByRole('heading', { name: 'OS 5101' })).toBeVisible()
  await expect(page.getByText('SN HWTC1A2B3C4D')).toBeVisible()
})

test('importação recusa .sql com orientação', async ({ page }) => {
  await simularApi(page, 'financeiro')
  await page.goto('/financeiro/importar')
  await page.getByLabel(/Escolher arquivo/).setInputFiles({ name: 'backup.sql', mimeType: 'application/sql', buffer: Buffer.from('SELECT 1') })
  await expect(page.getByRole('alert')).toContainText('exporte a consulta como .csv ou .json')
})

const previaFicticia = {
  importacaoId: '0192f000-0000-7000-8000-0000000000a1',
  arquivo: 'retiradas_outubro.csv',
  formato: 'csv',
  totalDeLinhas: 5,
  resumo: { novas: 3, atualizacoes: 0, repetidas: 0, duplicadas: 0, pendencias: 1, erros: 1, ordensNovas: 2, ordensAtualizadas: 0 },
  colunasReconhecidas: ['nome_razaosocial', 'tipo_ordem_servico', 'endereco', 'numero', 'bairro', 'cidade', 'estado', 'telefone_primario', 'id_ordem_servico'],
  colunasDescartadas: ['CPF/CNPJ', 'Plano'],
  avisos: ['O arquivo não traz latitude e longitude: as ordens novas aparecem só na lista, até o mapa ser ligado.'],
  linhas: [
    { numero: 2, resultado: 'nova', cliente: 'MARIA EXEMPLO SILVA', endereco: 'RUA DOS IPÊS, 120', bairro: 'CENTRO', tipoOs: 'REMOÇÃO EQUIP FTTH', detalhes: [] },
    { numero: 3, resultado: 'nova', cliente: 'MARIA EXEMPLO SILVA', endereco: 'RUA DOS IPÊS, 120', bairro: 'CENTRO', tipoOs: 'REMOÇÃO EQUIP FTTH', detalhes: ['mais um equipamento da OS da linha 2'] },
    { numero: 4, resultado: 'nova', cliente: 'MERCADO MODELO LTDA', endereco: 'RUA SABIÁ, 78', bairro: 'BELA VISTA', tipoOs: 'REMOÇÃO EQUIP FTTH', detalhes: ['alerta: telefone fora do padrão: (00) 9123'] },
    { numero: 5, resultado: 'pendencia', cliente: 'CARLOS SIMULADO LIMA', endereco: 'RUA DAS ACÁCIAS', bairro: 'BELA VISTA', tipoOs: 'REMOÇÃO EQUIP BLOQUEIO TEMPORARIO', detalhes: ['bloqueio temporário'] },
    { numero: 6, resultado: 'erro', cliente: 'RITA EXEMPLO MOURA', endereco: 'RUA DO CAMPO, 10', bairro: null, tipoOs: 'REMOÇÃO EQUIP FTTH', detalhes: ['falta o bairro'] },
  ],
}

test('importação mostra a prévia do servidor e grava só depois de confirmar', async ({ page }) => {
  await simularApi(page, 'financeiro')
  let gravou = false
  await page.route('**/api/importacoes/previa', async (rota) => {
    expect(rota.request().headers()['x-retirada']).toBe('1')
    await rota.fulfill({ json: previaFicticia })
  })
  await page.route('**/api/importacoes/*/confirmar', async (rota) => {
    gravou = true
    await rota.fulfill({
      json: { importacaoId: previaFicticia.importacaoId, resumo: previaFicticia.resumo, ordensCriadas: 2, ordensAtualizadas: 0, pendenciasRegistradas: 1, pendenciasResolvidas: 0 },
    })
  })

  await page.goto('/financeiro/importar')
  await page.getByLabel(/Escolher arquivo/).setInputFiles({ name: 'retiradas_outubro.csv', mimeType: 'text/csv', buffer: Buffer.from('a;b\n1;2') })
  await page.getByRole('button', { name: 'Ver a prévia' }).click()

  await expect(page.getByText('Ordens novas', { exact: true })).toBeVisible()
  await expect(page.getByText('2 colunas descartadas e nunca gravadas: CPF/CNPJ, Plano.', { exact: false })).toBeVisible()
  const axe = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()
  expect(axe.violations.map((v) => `${v.id}: ${v.help}`)).toEqual([])
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(page.viewportSize()!.width)
  await expect(page).toHaveScreenshot('importar-previa.png', { fullPage: true })
  expect(gravou).toBe(false) // nada é gravado antes da confirmação (RF03)

  await page.getByRole('button', { name: 'Erros · 1' }).click()
  await expect(page.getByText('falta o bairro').filter({ visible: true })).toBeVisible()

  await page.getByRole('button', { name: 'Gravar 2 ordens' }).click()
  await expect(page.getByText('Importação gravada')).toBeVisible()
  expect(gravou).toBe(true)
})
