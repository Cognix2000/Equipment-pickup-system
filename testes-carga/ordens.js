// Teste de carga da API de ordens (ADR-007), com metas tiradas do RNF01.
//
//   k6 run testes-carga/ordens.js                                  # carga normal, contra localhost:8080
//   BASE_URL=https://homologacao.exemplo k6 run testes-carga/ordens.js
//   CENARIO=estresse k6 run testes-carga/ordens.js                 # sobe até achar o limite
//   CENARIO=fumaca k6 run testes-carga/ordens.js                   # 1 usuário, só para conferir o script
//
// Nunca rode contra produção: use homologação, com os dados fictícios.
//
// Login (ADR-010): o script entra uma vez como o técnico fictício (EMAIL e SENHA mudam isso)
// e todos os usuários virtuais usam a mesma sessão. Como o limite da API é por usuário
// (300 por minuto), suba a homologação com Acesso__RequisicoesPorMinuto=100000 durante o teste.
import http from 'k6/http'
import { check, group, sleep } from 'k6'

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080'
const EMAIL = __ENV.EMAIL || 'tecnico.demo@exemplo.invalid'
const SENHA = __ENV.SENHA || 'Demo@Retirada2026'

// Uso esperado no incremento 1: até 30 técnicos olhando a lista e 5 pessoas no painel ao mesmo tempo.
const cenarios = {
  fumaca: { executor: 'constant-vus', vus: 1, duration: '10s' },
  carga: {
    executor: 'ramping-vus',
    stages: [
      { duration: '1m', target: 35 },
      { duration: '3m', target: 35 },
      { duration: '30s', target: 0 },
    ],
  },
  estresse: {
    executor: 'ramping-vus',
    stages: [
      { duration: '2m', target: 100 },
      { duration: '2m', target: 200 },
      { duration: '2m', target: 400 },
      { duration: '1m', target: 0 }, // e volta: o sistema precisa se recuperar sozinho
    ],
  },
}

const nome = __ENV.CENARIO || 'carga'

export const options = {
  scenarios: { [nome]: cenarios[nome] },
  thresholds: {
    // RNF01 pede a tela em até 2 s no 4G; a API fica com 500 ms no p95 para sobrar tempo à rede e à tela.
    'http_req_duration{tipo:lista}': ['p(95)<500'],
    'http_req_duration{tipo:detalhe}': ['p(95)<300'],
    http_req_failed: ['rate<0.01'],
    checks: ['rate>0.99'],
  },
}

export function setup() {
  const resposta = http.post(`${BASE_URL}/api/acesso/entrar`, JSON.stringify({ email: EMAIL, senha: SENHA }), {
    headers: { 'Content-Type': 'application/json', 'X-Retirada': '1' },
    tags: { tipo: 'login' },
  })
  const sessao = resposta.cookies.retirada_sessao?.[0]?.value
  if (resposta.status !== 200 || resposta.json('etapa') !== 'pronto' || !sessao) {
    throw new Error(`Login falhou (${resposta.status}). Use um usuário sem 2FA, como o técnico fictício.`)
  }
  return { sessao }
}

export default function ({ sessao }) {
  const jar = http.cookieJar()
  jar.set(BASE_URL, 'retirada_sessao', sessao)
  let ordens = []

  group('técnico abre a lista de ordens', () => {
    const resposta = http.get(`${BASE_URL}/api/ordens?status=atribuida`, { tags: { tipo: 'lista' } })
    check(resposta, {
      'lista responde 200': (r) => r.status === 200,
      'lista é JSON': (r) => r.headers['Content-Type']?.includes('application/json'),
    })
    ordens = resposta.status === 200 ? resposta.json() : []
  })

  sleep(1 + Math.random() * 2)

  if (ordens.length > 0) {
    group('técnico abre o detalhe', () => {
      const ordem = ordens[Math.floor(Math.random() * ordens.length)]
      const resposta = http.get(`${BASE_URL}/api/ordens/${ordem.id}`, { tags: { tipo: 'detalhe' } })
      check(resposta, { 'detalhe responde 200': (r) => r.status === 200 })
    })
  }

  sleep(2 + Math.random() * 3)
}
