import type { Ordem } from './tipos'

/**
 * Ordens fictícias, iguais às do protótipo e da carga de homologação (db/dados-ficticios/demo.sql).
 * Usadas no Storybook e nos testes de tela. Nenhum dado real.
 */
const hoje = new Date()
const emDias = (dias: number) => new Date(hoje.getTime() + dias * 86_400_000).toISOString()

export const ordemMaria: Ordem = {
  id: '0192f000-0000-7000-8000-000000005101',
  numeroOs: 5101,
  tipoOs: 'REMOÇÃO EQUIP FTTH',
  status: 'atribuida',
  cliente: 'MARIA EXEMPLO SILVA',
  endereco: { logradouro: 'RUA DOS IPÊS', numero: '120', complemento: 'CASA 2', bairro: 'CENTRO', cidade: 'CIDADE SIMULADA', uf: 'MG', referencia: 'PORTÃO AZUL' },
  localizacao: { latitude: -21.0012, longitude: -38.9978 },
  observacao: 'Cliente só fica em casa à tarde',
  plano: '600 Mbps',
  carenciaAte: emDias(-1),
  aguardaBaixaNoHubsoft: false,
  equipamentos: [
    { id: '0192f000-0000-7000-8000-000000051011', tipo: 'onu', modelo: 'Huawei EG8010H', numeroSerie: 'HWTC1A2B3C4D', situacao: 'pendente' },
    { id: '0192f000-0000-7000-8000-000000051012', tipo: 'roteador', modelo: 'TP-Link Archer C6', numeroSerie: '22361A2B3C4D5', situacao: 'pendente' },
    { id: '0192f000-0000-7000-8000-000000051013', tipo: 'fonte', modelo: 'Fonte 12V', numeroSerie: null, situacao: 'pendente' },
  ],
  telefones: [
    { id: '0192f000-0000-7000-8000-0000000051f1', numero: '+5500912345678', tipo: 'celular', formatoValido: true, linha: 'nao_verificada', whatsapp: 'nao_verificado' },
    { id: '0192f000-0000-7000-8000-0000000051f2', numero: '+55009123', tipo: 'desconhecido', formatoValido: false, linha: 'nao_verificada', whatsapp: 'nao_verificado' },
  ],
  historico: [
    { tipo: 'contato', ocorridoEm: '2026-09-16T12:40:00Z', motivo: 'nao_atendeu', statusPara: 'atribuida', atorPapel: 'tecnico' },
    { tipo: 'designada', ocorridoEm: '2026-09-16T11:02:00Z', motivo: null, statusPara: 'atribuida', atorPapel: 'admin' },
    { tipo: 'carencia_vencida', ocorridoEm: '2026-09-15T13:15:00Z', motivo: null, statusPara: 'disponivel', atorPapel: null },
    { tipo: 'importada', ocorridoEm: '2026-09-13T13:15:00Z', motivo: null, statusPara: 'nova', atorPapel: 'financeiro' },
  ],
}

export const ordemJoao: Ordem = {
  ...ordemMaria,
  id: '0192f000-0000-7000-8000-000000005102',
  numeroOs: 5102,
  tipoOs: 'REMOÇÃO EQUIP INADIMPLENTE',
  cliente: 'JOÃO FICTÍCIO SOUZA',
  endereco: { logradouro: 'AVENIDA DA LAGOA', numero: '455', complemento: null, bairro: 'CENTRO', cidade: 'CIDADE SIMULADA', uf: 'MG', referencia: null },
  localizacao: { latitude: -21.0031, longitude: -38.9921 },
  observacao: null,
  plano: null,
  carenciaAte: emDias(0),
  equipamentos: [{ id: '0192f000-0000-7000-8000-000000051021', tipo: 'ont', modelo: 'Huawei EG8145V5', numeroSerie: 'HWTC5E6F7A8B', situacao: 'pendente' }],
}

export const ordemMercado: Ordem = {
  ...ordemJoao,
  id: '0192f000-0000-7000-8000-000000005103',
  numeroOs: 5103,
  tipoOs: 'REMOÇÃO EQUIP FTTH',
  status: 'conferida',
  cliente: 'MERCADO MODELO LTDA',
  endereco: { logradouro: 'RUA SABIÁ', numero: '78', complemento: null, bairro: 'BELA VISTA', cidade: 'CIDADE SIMULADA', uf: 'MG', referencia: null },
  localizacao: { latitude: -20.9949, longitude: -38.9958 },
  carenciaAte: emDias(-2),
  aguardaBaixaNoHubsoft: true,
  equipamentos: [{ id: '0192f000-0000-7000-8000-000000051031', tipo: 'ont', modelo: 'TP-Link XX230v', numeroSerie: 'TPLG9C0D1E2F', situacao: 'conferido' }],
}

export const ordemCarlos: Ordem = {
  ...ordemJoao,
  id: '0192f000-0000-7000-8000-000000005110',
  numeroOs: 5110,
  tipoOs: 'REMOÇÃO EQUIP FTTH',
  status: 'nova',
  cliente: 'CARLOS SIMULADO LIMA',
  endereco: { logradouro: 'RUA DAS ACÁCIAS', numero: 'S/N', complemento: null, bairro: 'BELA VISTA', cidade: 'CIDADE SIMULADA', uf: 'MG', referencia: null },
  localizacao: null,
  carenciaAte: emDias(2),
  equipamentos: [{ id: '0192f000-0000-7000-8000-000000051101', tipo: 'ont', modelo: 'Huawei EG8145V5', numeroSerie: 'HWTC7C8D9E0F', situacao: 'pendente' }],
}

/** Disponível há 12 dias sem nenhuma tentativa de contato: não encerra sozinha, o financeiro precisa agir (RN09). */
export const ordemAna: Ordem = {
  ...ordemMaria,
  id: '0192f000-0000-7000-8000-000000005105',
  numeroOs: 5105,
  status: 'disponivel',
  cliente: 'ANA FICTÍCIA COSTA',
  endereco: { logradouro: 'RUA DAS PALMEIRAS', numero: '88', complemento: null, bairro: 'JARDIM', cidade: 'CIDADE SIMULADA', uf: 'MG', referencia: null },
  localizacao: null,
  observacao: null,
  plano: null,
  carenciaAte: emDias(-12),
  responsavelId: null,
  equipamentos: [{ id: '0192f000-0000-7000-8000-000000051051', tipo: 'onu', modelo: 'Huawei EG8010H', numeroSerie: 'HWTC7A7B7C7D', situacao: 'pendente' }],
  semContatoNoPrazo: true,
}

export const todasAsOrdens = [ordemMaria, ordemJoao, ordemMercado, ordemCarlos]
