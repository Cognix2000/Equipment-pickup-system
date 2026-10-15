import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page } from '@playwright/test'
import { ordemMaria, todasAsOrdens } from '../src/features/ordens/exemplos'

type Papel = 'tecnico' | 'financeiro' | 'admin' | 'estoque' | null

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
    const busca = url.searchParams.get('busca')?.replace(/\s+/g, '').toUpperCase()
    const encontradas = todasAsOrdens
      .filter((o) => !status || o.status === status)
      .filter((o) => !busca || String(o.numeroOs) === busca || o.equipamentos.some((e) => e.numeroSerie === busca))
    return rota.fulfill({ json: encontradas })
  })
}

const telas: { nome: string; rota: string; pronto: string; papel: Papel }[] = [
  { nome: 'entrar', rota: '/entrar', pronto: 'Esqueceu a senha?', papel: null },
  { nome: 'primeiro-acesso', rota: '/primeiro-acesso#t=token-ficticio', pronto: 'Crie sua senha', papel: null },
  { nome: 'minhas-ordens', rota: '/tecnico/ordens', pronto: 'Ver ordem', papel: 'tecnico' },
  { nome: 'detalhe-da-ordem', rota: `/tecnico/ordens/${ordemMaria.id}`, pronto: 'Recolher 3 itens', papel: 'tecnico' },
  { nome: 'registrar-retirada', rota: `/tecnico/ordens/${ordemMaria.id}/retirada`, pronto: 'Série da etiqueta', papel: 'tecnico' },
  { nome: 'visita-sem-sucesso', rota: `/tecnico/ordens/${ordemMaria.id}/visita`, pronto: 'O que aconteceu?', papel: 'tecnico' },
  { nome: 'ordens-do-financeiro', rota: '/financeiro/ordens', pronto: 'MARIA EXEMPLO SILVA', papel: 'financeiro' },
  { nome: 'importar-planilha', rota: '/financeiro/importar', pronto: 'Escolher arquivo', papel: 'financeiro' },
  { nome: 'usuarios', rota: '/gestao/usuarios', pronto: 'Cadastrar e gerar convite', papel: 'admin' },
  { nome: 'conferir-no-estoque', rota: '/estoque/conferir', pronto: 'Protocolo, número da OS ou série de um item', papel: 'estoque' },
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
        // O mapa depende da GPU, e os telefones mudam com a hora (RN10): a comparação fica para o resto da tela.
        mask: [page.locator('.maplibregl-map'), page.locator('[data-teste="telefones"]'), page.locator('[data-teste="hora"]')],
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

/** Responde aos comandos da ordem e guarda o que chegou, para o teste conferir. */
async function simularComando(page: Page, acao: string, resposta: object) {
  const recebido: { chave?: string; corpo?: Record<string, unknown> } = {}
  await page.route(`**/api/ordens/*/${acao}`, async (rota) => {
    recebido.chave = rota.request().headers()['idempotency-key']
    recebido.corpo = rota.request().postDataJSON()
    await rota.fulfill({ json: resposta })
  })
  return recebido
}

test('técnico registra a retirada com série, GPS e chave de idempotência', async ({ page, context }) => {
  await simularApi(page, 'tecnico')
  await context.grantPermissions(['geolocation'])
  await context.setGeolocation({ latitude: -21.0012, longitude: -38.9978, accuracy: 8 })
  const recebido = await simularComando(page, 'retirada', { ...ordemMaria, status: 'concluida' })

  await page.goto(`/tecnico/ordens/${ordemMaria.id}`)
  await page.getByRole('link', { name: 'Iniciar retirada' }).click()
  await expect(page.getByRole('button', { name: 'Confirmar retirada · faltam 3 itens' })).toBeDisabled()

  const series = page.getByLabel(/Série da etiqueta/)
  await series.nth(0).fill('hwtc 1a2b3c4d') // como o técnico digita: minúscula e espaço
  await expect(page.getByText('Série confere')).toBeVisible()
  await series.nth(1).fill('OUTRASERIE')
  await expect(page.getByText('vai registrada como divergência')).toBeVisible()
  await page.getByLabel('Recolhida').check()
  await page.getByRole('button', { name: 'Confirmar retirada', exact: true }).click()

  await expect(page.getByText('Retirada registrada.')).toBeVisible()
  expect(recebido.chave).toMatch(/^[0-9a-f-]{36}$/)
  expect(recebido.corpo).toMatchObject({
    gps: { latitude: -21.0012, longitude: -38.9978, precisaoM: 8 },
    itens: [
      { equipamentoId: ordemMaria.equipamentos[0].id, numeroSerie: 'hwtc 1a2b3c4d' },
      { equipamentoId: ordemMaria.equipamentos[1].id, numeroSerie: 'OUTRASERIE' },
      { equipamentoId: ordemMaria.equipamentos[2].id, numeroSerie: null },
    ],
  })
})

test('ordem cancelada bloqueia a retirada (RN07)', async ({ page }) => {
  await simularApi(page, 'tecnico')
  await page.route(`**/api/ordens/${ordemMaria.id}`, (rota) => rota.fulfill({ json: { ...ordemMaria, status: 'cancelada' } }))

  await page.goto(`/tecnico/ordens/${ordemMaria.id}/retirada`)

  await expect(page.getByRole('alert')).toContainText('a retirada está bloqueada')
  await expect(page.getByRole('button', { name: /Confirmar retirada/ })).toHaveCount(0)
})

test('técnico registra visita sem sucesso com motivo e GPS', async ({ page, context }) => {
  await simularApi(page, 'tecnico')
  await context.grantPermissions(['geolocation'])
  await context.setGeolocation({ latitude: -21.0012, longitude: -38.9978, accuracy: 12 })
  const recebido = await simularComando(page, 'visita-sem-sucesso', { ...ordemMaria, status: 'disponivel', responsavelId: null, visitasSemSucesso: 1 })

  await page.goto(`/tecnico/ordens/${ordemMaria.id}/visita`)
  await expect(page.getByRole('button', { name: 'Registrar visita' })).toBeDisabled()
  await page.getByLabel(/Endereço não encontrado/).check()
  await page.getByRole('button', { name: 'Registrar visita' }).click()

  await expect(page.getByText('A ordem voltou para a fila.')).toBeVisible()
  expect(recebido.corpo).toMatchObject({ motivo: 'nao_achou', gps: { precisaoM: 12 } })
})

test('sem permissão de localização, a retirada não segue e explica por quê', async ({ page }) => {
  await simularApi(page, 'tecnico')
  // O Chromium sem tela deixa o pedido de permissão pendente; aqui a pessoa nega.
  await page.addInitScript(() => {
    navigator.geolocation.getCurrentPosition = (_ok, erro) =>
      erro?.({ code: 1, message: 'negado', PERMISSION_DENIED: 1, POSITION_UNAVAILABLE: 2, TIMEOUT: 3 } as GeolocationPositionError)
  })
  const recebido = await simularComando(page, 'retirada', ordemMaria)

  await page.goto(`/tecnico/ordens/${ordemMaria.id}/retirada`)
  const series = page.getByLabel(/Série da etiqueta/)
  await series.nth(0).fill('HWTC1A2B3C4D')
  await series.nth(1).fill('22361A2B3C4D5')
  await page.getByLabel('Recolhida').check()
  await page.getByRole('button', { name: 'Confirmar retirada', exact: true }).click()

  await expect(page.getByRole('alert')).toContainText('Permita o uso da localização')
  expect(recebido.corpo).toBeUndefined()
})

test('ligar só aparece na janela de contato (RN10)', async ({ page }) => {
  await simularApi(page, 'tecnico')

  await page.clock.setFixedTime(new Date('2026-10-14T14:00:00Z')) // quarta, 11h em Brasília
  await page.goto(`/tecnico/ordens/${ordemMaria.id}`)
  await expect(page.getByRole('link', { name: 'Ligar' })).toHaveCount(1) // só o número no padrão

  await page.clock.setFixedTime(new Date('2026-10-18T14:00:00Z')) // domingo
  await page.reload()
  await expect(page.getByText('Contato só de segunda a sábado, das 8h às 18h.')).toBeVisible()
  await expect(page.getByRole('link', { name: 'Ligar' })).toHaveCount(0)
})

test('depois de ligar, o técnico registra como foi e o número inexistente sai da lista (RN10)', async ({ page }) => {
  await simularApi(page, 'tecnico')
  await page.clock.setFixedTime(new Date('2026-10-14T14:00:00Z')) // quarta, 11h em Brasília
  const [celular, foraDoPadrao] = ordemMaria.telefones!
  const depois = {
    ...ordemMaria,
    telefones: [{ ...celular, linha: 'inexistente' }, foraDoPadrao],
    historico: [
      { tipo: 'contato', ocorridoEm: '2026-10-14T14:00:00Z', motivo: 'numero_inexistente', statusPara: 'atribuida', atorPapel: 'tecnico' },
      ...ordemMaria.historico!,
    ],
  }
  const recebido = await simularComando(page, 'contatos', depois)
  // No celular, o tel: abre o discador. No Chromium sem tela, ele abre um pedido de protocolo externo
  // que segura os cliques seguintes; aqui o link só registra o toque, como se o discador abrisse.
  await page.addInitScript(() =>
    document.addEventListener('click', (e) => {
      if ((e.target as Element).closest('a[href^="tel:"]')) e.preventDefault()
    }),
  )

  await page.goto(`/tecnico/ordens/${ordemMaria.id}`)
  await page.getByRole('link', { name: 'Ligar' }).click()
  const resultado = page.getByRole('group', { name: 'Como foi a ligação para (00) 91234-5678?' })
  await expect(resultado.getByRole('button')).toHaveText(['Atendeu', 'Não atendeu', 'Caixa postal', 'Número não existe'])

  const axe = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()
  expect(axe.violations.map((v) => `${v.id}: ${v.help}`)).toEqual([])
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(page.viewportSize()!.width)

  // Depois do registro, a tela volta a buscar a ordem: o servidor já devolve o número marcado.
  await page.route(`**/api/ordens/${ordemMaria.id}`, (rota) => rota.fulfill({ json: depois }))
  await resultado.getByRole('button', { name: 'Número não existe' }).click()

  await expect(page.getByRole('status')).toHaveText('Ligação registrada no histórico.')
  expect(recebido.chave).toMatch(/^[0-9a-f-]{36}$/)
  expect(recebido.corpo).toEqual({ telefoneId: celular.id, canal: 'ligacao', resultado: 'numero_inexistente' })
  await expect(page.getByText('(00) 91234-5678 · número não existe')).toBeVisible()
  await expect(page.getByRole('link', { name: 'Ligar' })).toHaveCount(0)
  await expect(page.getByText('Contato com o cliente: número não existe pelo técnico')).toBeVisible()
})

test('estoque acha a ordem pela série, confere e registra a divergência (UC06)', async ({ page }) => {
  await simularApi(page, 'estoque')
  const entregue = { ...ordemMaria, status: 'concluida' }
  await page.route('**/api/ordens?busca=*', (rota) => rota.fulfill({ json: [entregue] }))
  const recebido = await simularComando(page, 'conferencia', {
    ...entregue,
    equipamentos: entregue.equipamentos.map((e, i) => ({ ...e, situacao: i === 1 ? 'divergente' : 'conferido' })),
  })

  await page.goto('/estoque/conferir')
  await page.getByLabel('Protocolo, número da OS ou série de um item').fill('HWTC1A2B3C4D')
  await page.getByRole('button', { name: 'Buscar' }).click()
  await expect(page.getByRole('button', { name: 'Falta ler 2 itens' })).toBeDisabled()

  const series = page.getByLabel(/Série lida/)
  await series.nth(0).fill('hwtc1a2b3c4d')
  await expect(page.getByText('Confere', { exact: true })).toBeVisible()
  await page.getByLabel(/Não veio/).nth(1).check()

  const axe = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()
  expect(axe.violations.map((v) => `${v.id}: ${v.help}`)).toEqual([])
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(page.viewportSize()!.width)
  await expect(page).toHaveScreenshot('conferir-ordem.png', { fullPage: true })

  await page.getByRole('button', { name: 'Registrar 1 divergência' }).click()
  await expect(page.getByText('Divergência registrada: a ordem continua aberta.')).toBeVisible()
  expect(recebido.chave).toMatch(/^[0-9a-f-]{36}$/)
  expect(recebido.corpo).toEqual({
    itens: [
      { equipamentoId: entregue.equipamentos[0].id, numeroSerie: 'hwtc1a2b3c4d' },
      { equipamentoId: entregue.equipamentos[1].id, numeroSerie: null },
      { equipamentoId: entregue.equipamentos[2].id, numeroSerie: null },
    ],
  })
})

test('estoque registra devolução na loja com confirmação (RF15)', async ({ page }) => {
  await simularApi(page, 'estoque')
  const recebido = await simularComando(page, 'devolucao-na-loja', { ...ordemMaria, status: 'devolvida_na_loja' })

  await page.goto('/estoque/conferir')
  await page.getByLabel('Protocolo, número da OS ou série de um item').fill('5101')
  await page.getByRole('button', { name: 'Buscar' }).click()
  await page.getByRole('button', { name: 'Registrar devolução' }).click()
  await page.getByRole('button', { name: 'Confirmar devolução' }).click()

  await expect(page.getByText('Devolução na loja registrada.', { exact: false })).toBeVisible()
  expect(recebido.chave).toBeDefined()
})

test('estoque entra direto na conferência e técnico não abre o estoque', async ({ page }) => {
  await simularApi(page, 'estoque')
  await page.goto('/')
  await expect(page).toHaveURL(/\/estoque\/conferir/)

  await simularApi(page, 'tecnico')
  await page.goto('/estoque/conferir')
  await expect(page).toHaveURL(/\/tecnico\/ordens/)
})
