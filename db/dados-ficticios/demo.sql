-- Carga fictícia para homologação: as ordens dos protótipos de tela.
-- Nada aqui é dado real: DDD 00, e-mail .invalid, nomes e séries inventados, cidade simulada.
-- Idempotente: rodar de novo não duplica (ON CONFLICT DO NOTHING).
-- NUNCA aplicar em produção: o migrador só carrega este arquivo com --dados-ficticios.
SET search_path = retirada, public;

INSERT INTO usuario (id, nome, email, papel, situacao) VALUES
    ('0192f000-0000-7000-8000-000000000001', 'Técnico Demonstração', 'tecnico.demo@exemplo.invalid', 'tecnico',    'ativo'),
    ('0192f000-0000-7000-8000-000000000002', 'Financeiro Demonstração', 'financeiro.demo@exemplo.invalid', 'financeiro', 'ativo'),
    ('0192f000-0000-7000-8000-000000000003', 'Estoque Demonstração', 'estoque.demo@exemplo.invalid', 'estoque',    'ativo'),
    ('0192f000-0000-7000-8000-000000000004', 'Gestor Demonstração', 'gestor.demo@exemplo.invalid', 'admin',      'ativo')
ON CONFLICT DO NOTHING;
-- As senhas não ficam aqui: o migrador com --dados-ficticios define a senha de demonstração (README).

INSERT INTO cliente (id, codigo_hubsoft, nome, tipo_pessoa) VALUES
    ('0192f000-0000-7000-8000-000000000101', 1001, 'MARIA EXEMPLO SILVA',  'PF'),
    ('0192f000-0000-7000-8000-000000000102', 1002, 'JOÃO FICTÍCIO SOUZA',  'PF'),
    ('0192f000-0000-7000-8000-000000000103', 1003, 'MERCADO MODELO LTDA',  'PJ'),
    ('0192f000-0000-7000-8000-000000000104', 1004, 'CARLOS SIMULADO LIMA', 'PF')
ON CONFLICT DO NOTHING;

INSERT INTO telefone_cliente (cliente_id, numero_e164, como_veio, formato_valido)
SELECT v.cliente_id::uuid, v.numero, v.como_veio, false
  FROM (VALUES
    ('0192f000-0000-7000-8000-000000000101', '+5500912345678', '(00) 91234-5678'),
    ('0192f000-0000-7000-8000-000000000102', '+5500923456789', '(00) 92345-6789'),
    ('0192f000-0000-7000-8000-000000000103', '+5500934567890', '(00) 93456-7890'),
    ('0192f000-0000-7000-8000-000000000104', '+5500945678901', '(00) 94567-8901')) AS v(cliente_id, numero, como_veio)
 WHERE NOT EXISTS (SELECT 1 FROM telefone_cliente t WHERE t.cliente_id = v.cliente_id::uuid);

INSERT INTO ordem (id, chave_importacao, numero_os, tipo_os, cliente_id, logradouro, numero, complemento, bairro, cidade, uf,
                   referencia, observacao, localizacao, status, carencia_ate, responsavel_id, encerrada_em, criado_em)
VALUES
    ('0192f000-0000-7000-8000-000000005101', 'demo-5101', 5101, 'REMOÇÃO EQUIP FTTH', '0192f000-0000-7000-8000-000000000101',
     'RUA DOS IPÊS', '120', 'CASA 2', 'CENTRO', 'CIDADE SIMULADA', 'MG', 'PORTÃO AZUL', 'Cliente só fica em casa à tarde',
     ST_GeogFromText('POINT(-38.9978 -21.0012)'), 'atribuida', now() - interval '1 day',
     '0192f000-0000-7000-8000-000000000001', NULL, now() - interval '3 days'),
    ('0192f000-0000-7000-8000-000000005102', 'demo-5102', 5102, 'REMOÇÃO EQUIP INADIMPLENTE', '0192f000-0000-7000-8000-000000000102',
     'AVENIDA DA LAGOA', '455', NULL, 'CENTRO', 'CIDADE SIMULADA', 'MG', NULL, NULL,
     ST_GeogFromText('POINT(-38.9921 -21.0031)'), 'atribuida', now(),
     '0192f000-0000-7000-8000-000000000001', NULL, now() - interval '2 days'),
    ('0192f000-0000-7000-8000-000000005103', 'demo-5103', 5103, 'REMOÇÃO EQUIP FTTH', '0192f000-0000-7000-8000-000000000103',
     'RUA SABIÁ', '78', NULL, 'BELA VISTA', 'CIDADE SIMULADA', 'MG', NULL, NULL,
     ST_GeogFromText('POINT(-38.9958 -20.9949)'), 'conferida', now() - interval '2 days',
     NULL, now() - interval '3 hours', now() - interval '4 days'),
    ('0192f000-0000-7000-8000-000000005110', 'demo-5110', 5110, 'REMOÇÃO EQUIP FTTH', '0192f000-0000-7000-8000-000000000104',
     'RUA DAS ACÁCIAS', 'S/N', NULL, 'BELA VISTA', 'CIDADE SIMULADA', 'MG', NULL, NULL,
     NULL, 'nova', now() + interval '31 hours', NULL, NULL, now() - interval '17 hours')
ON CONFLICT DO NOTHING;

INSERT INTO equipamento_ordem (id, ordem_id, tipo, modelo, numero_serie, situacao, conferido_por, conferido_em) VALUES
    ('0192f000-0000-7000-8000-000000051011', '0192f000-0000-7000-8000-000000005101', 'ONU',      'Huawei EG8010H',    'HWTC1A2B3C4D',  'pendente', NULL, NULL),
    ('0192f000-0000-7000-8000-000000051012', '0192f000-0000-7000-8000-000000005101', 'Roteador', 'TP-Link Archer C6', '22361A2B3C4D5', 'pendente', NULL, NULL),
    ('0192f000-0000-7000-8000-000000051013', '0192f000-0000-7000-8000-000000005101', 'Fonte',    'Fonte 12V',         NULL,            'pendente', NULL, NULL),
    ('0192f000-0000-7000-8000-000000051021', '0192f000-0000-7000-8000-000000005102', 'ONT',      'Huawei EG8145V5',   'HWTC5E6F7A8B',  'pendente', NULL, NULL),
    ('0192f000-0000-7000-8000-000000051031', '0192f000-0000-7000-8000-000000005103', 'ONT',      'TP-Link XX230v',    'TPLG9C0D1E2F',  'conferido',
     '0192f000-0000-7000-8000-000000000003', now() - interval '3 hours'),
    ('0192f000-0000-7000-8000-000000051101', '0192f000-0000-7000-8000-000000005110', 'ONT',      'Huawei EG8145V5',   'HWTC7C8D9E0F',  'pendente', NULL, NULL)
ON CONFLICT DO NOTHING;
