-- Testes de comportamento do modelo (rodar depois de modelo_de_dados.sql, num banco de TESTE):
--   createdb teste_retirada
--   psql -q -v ON_ERROR_STOP=1 -d teste_retirada -f modelo_de_dados.sql
--   psql -d teste_retirada -f teste_modelo_de_dados.sql
-- Cada linha marcada "deve falhar" precisa terminar em ERROR; as demais mostram o resultado esperado.
\set ON_ERROR_STOP 0
SET search_path = retirada, public;

-- dados mínimos
INSERT INTO empresa_parceira (id, razao_social, documento_cifrado, contrato_versao, contrato_assinado_em, situacao) VALUES
 ('40000000-0000-0000-0000-000000000001', 'REDE EXEMPLO SERVIÇOS LTDA', '\x00', 'v1', now(), 'ativo');
INSERT INTO usuario (id, nome, email, papel, situacao, empresa_parceira_id) VALUES
 ('00000000-0000-0000-0000-000000000001', 'Financeiro Teste', 'fin@exemplo.invalid', 'financeiro', 'ativo', NULL),
 ('00000000-0000-0000-0000-000000000002', 'Parceiro A', 'pa@exemplo.invalid', 'parceiro', 'ativo', '40000000-0000-0000-0000-000000000001'),
 ('00000000-0000-0000-0000-000000000003', 'Parceiro B', 'pb@exemplo.invalid', 'parceiro', 'ativo', '40000000-0000-0000-0000-000000000001');
INSERT INTO usuario (nome, email, papel, situacao) VALUES ('Sem Empresa', 'se@exemplo.invalid', 'parceiro', 'ativo'); -- deve falhar: parceiro sem empresa
INSERT INTO cliente (id, codigo_hubsoft, nome, tipo_pessoa, criado_em) VALUES ('10000000-0000-0000-0000-000000000001', 1001, 'CLIENTE FICTICIO', 'PF', now() - interval '7 years');
INSERT INTO telefone_cliente (cliente_id, numero_e164, como_veio, formato_valido) VALUES ('10000000-0000-0000-0000-000000000001', '+5500912345678', '(00) 91234-5678', false);
INSERT INTO ordem (id, chave_importacao, tipo_os, protocolo, cliente_id, logradouro, bairro, cidade, uf, data_rescisao, carencia_ate, localizacao, status)
 VALUES ('20000000-0000-0000-0000-000000000001','os-9101','REMOÇÃO EQUIP FTTH','20260914101500123456','10000000-0000-0000-0000-000000000001','RUA DOS IPÊS','CENTRO','CIDADE SIMULADA','MG','2026-09-14','2026-09-16 10:15-03', ST_GeogFromText('POINT(-39.0 -21.0)'), 'disponivel');
INSERT INTO ordem (id, chave_importacao, tipo_os, protocolo, cliente_id, logradouro, bairro, cidade, uf, data_rescisao, carencia_ate, status)
 VALUES ('20000000-0000-0000-0000-000000000002','os-9102','REMOÇÃO EQUIP FTTH','20260915101500123456','10000000-0000-0000-0000-000000000001','RUA DOS IPÊS','CENTRO','CIDADE SIMULADA','MG','2026-09-15','2026-09-17 10:15-03', 'disponivel');

-- 1) aceite único: dois aceites com a mesma versão, só um vence
UPDATE ordem SET status='atribuida', responsavel_id='00000000-0000-0000-0000-000000000002', versao=versao+1 WHERE id='20000000-0000-0000-0000-000000000001' AND status='disponivel' AND versao=1;
UPDATE ordem SET status='atribuida', responsavel_id='00000000-0000-0000-0000-000000000003', versao=versao+1 WHERE id='20000000-0000-0000-0000-000000000001' AND status='disponivel' AND versao=1;
SELECT 'aceite_unico' AS teste, responsavel_id, versao FROM ordem WHERE id='20000000-0000-0000-0000-000000000001';

-- 2) evento só de acréscimo e idempotente
INSERT INTO evento_ordem (ordem_id, seq, tipo, ocorrido_em, chave_idempotencia, local) VALUES ('20000000-0000-0000-0000-000000000001', 1, 'aceita', now(), '30000000-0000-0000-0000-000000000001', ST_GeogFromText('POINT(-39.0 -21.0)'));
INSERT INTO evento_ordem (ordem_id, seq, tipo, ocorrido_em, chave_idempotencia) VALUES ('20000000-0000-0000-0000-000000000001', 2, 'aceita', now(), '30000000-0000-0000-0000-000000000001');  -- deve falhar: reenvio (idempotência)
UPDATE evento_ordem SET tipo='x';  -- deve falhar: só acréscimo

-- 3) status de encerramento exige encerrada_em
UPDATE ordem SET status='conferida' WHERE id='20000000-0000-0000-0000-000000000001'; -- deve falhar
UPDATE ordem SET status='conferida', encerrada_em = now() - interval '6 years' WHERE id='20000000-0000-0000-0000-000000000001';

-- 4) protocolo inválido e exceção sem justificativa (protocolo agora é opcional, mas se vier tem 20 dígitos)
INSERT INTO ordem (chave_importacao, tipo_os, protocolo, cliente_id, logradouro, bairro, cidade, uf, data_rescisao, carencia_ate) VALUES ('x1','REMOÇÃO EQUIP FTTH','123','10000000-0000-0000-0000-000000000001','R','B','C','MG','2026-09-14', now()); -- deve falhar
INSERT INTO ordem (chave_importacao, tipo_os, protocolo, cliente_id, logradouro, bairro, cidade, uf, aberta_por_excecao, carencia_ate) VALUES ('x2','REMOÇÃO EQUIP FTTH','20260914101500999999','10000000-0000-0000-0000-000000000001','R','B','C','MG', true, now()); -- deve falhar

-- 5) mesma série não pode estar em aberto em duas ordens
INSERT INTO equipamento_ordem (ordem_id, tipo, modelo, numero_serie) VALUES ('20000000-0000-0000-0000-000000000002', 'ONT', 'TP-Link XX230v', 'TPLG9C0D1E2F');
INSERT INTO equipamento_ordem (ordem_id, tipo, modelo) VALUES ('20000000-0000-0000-0000-000000000002', 'Fonte', 'Fonte 12V 1A');
INSERT INTO equipamento_ordem (ordem_id, tipo, modelo) VALUES ('20000000-0000-0000-0000-000000000002', 'ONU', 'Huawei EG8010H'); -- deve falhar: ONU sem série
INSERT INTO equipamento_ordem (ordem_id, tipo, modelo, numero_serie) VALUES ('20000000-0000-0000-0000-000000000001', 'ONT', 'TP-Link XX230v', 'TPLG9C0D1E2F'); -- deve falhar

-- 6) fechamento: total do mês e saldo negativo transportado
INSERT INTO fechamento_parceiro (empresa_parceira_id, mes, aparelhos_conferidos, valor_por_aparelho, bonus_meta, bonus_reversao, descontos) VALUES ('40000000-0000-0000-0000-000000000001','2026-10-01', 40, 10.00, 100.00, 50.00, 20.00);
INSERT INTO fechamento_parceiro (empresa_parceira_id, mes, aparelhos_conferidos, valor_por_aparelho, descontos) VALUES ('40000000-0000-0000-0000-000000000001','2026-11-01', 5, 10.00, 460.00);
SELECT 'fechamento' AS teste, mes, a_pagar, a_transportar FROM fechamento_parceiro ORDER BY mes;

-- 7) confirmação: quem pede não pode ser quem registra
INSERT INTO confirmacao_telefone (ordem_id, solicitada_por, registrada_por, decisao, codigo_hmac, valida_ate) VALUES ('20000000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000002','00000000-0000-0000-0000-000000000002','pode_retirar', repeat('a',64), now()+interval '2 hours'); -- deve falhar

-- 8) evidência imutável; pendência de importação
INSERT INTO evidencia (ordem_id, tipo, arquivo_chave, sha256, bytes, mime, capturada_em, autor_id) VALUES ('20000000-0000-0000-0000-000000000001','etiqueta_serie','ordens/2000/1.jpg', repeat('b',64), 1000, 'image/jpeg', now(), '00000000-0000-0000-0000-000000000002');
UPDATE evidencia SET sha256 = repeat('c',64); -- deve falhar: só acréscimo
INSERT INTO importacao (id, arquivo_nome, arquivo_sha256, enviado_por, linhas_total) VALUES ('50000000-0000-0000-0000-000000000001', 'planilha.xlsx', repeat('d',64), '00000000-0000-0000-0000-000000000001', 61);
INSERT INTO pendencia_importacao (importacao_id, chave_importacao, motivo, criada_em) VALUES ('50000000-0000-0000-0000-000000000001', 'provisoria-ab12cd34', 'bloqueio_temporario', now() - interval '6 years');
INSERT INTO auditoria_acesso (usuario_id, acao, em) VALUES ('00000000-0000-0000-0000-000000000001', 'login', now() - interval '6 years');

-- 9) a aplicação não apaga; o expurgo usa UM corte nos dois passos
SET ROLE retirada_app;
DELETE FROM ordem; -- deve falhar: a aplicação não apaga
RESET ROLE;
SET ROLE retirada_expurgo;
SELECT 'arquivos_a_apagar' AS teste, arquivo_chave FROM retirada.arquivos_a_expurgar(now() - interval '5 years');
SELECT 'expurgo' AS teste, * FROM retirada.expurgar(now() - interval '5 years');
RESET ROLE;
SELECT 'restam' AS teste, (SELECT count(*) FROM ordem) AS ordens, (SELECT count(*) FROM cliente) AS clientes,
       (SELECT count(*) FROM evento_ordem) AS eventos, (SELECT count(*) FROM evidencia) AS evidencias,
       (SELECT count(*) FROM auditoria_acesso) AS auditorias, (SELECT count(*) FROM pendencia_importacao) AS pendencias;
