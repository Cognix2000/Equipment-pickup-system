-- Limpeza para entrar em produção de verdade.
--
-- Zera os dados fictícios que a aplicação carrega na homologação (demo.sql) e deixa
-- APENAS o usuário gestor informado em `:gestor_email` como `admin`/`ativo`. Preserva
-- as chaves de acesso dele (passkeys) e a configuração em `parametro`.
--
-- Como rodar (no host de produção, via container do banco):
--
--   docker compose cp db/limpar-para-producao.sql banco:/tmp/limpar.sql
--   docker compose exec -T banco psql -U retirada_dono -d retirada \
--       -v gestor_email='pedromendes211205@gmail.com' \
--       -f /tmp/limpar.sql
--
-- Pré-requisito: o usuário gestor JÁ precisa existir no banco com o e-mail informado.
-- Se não existir, o script aborta sem apagar nada.
-- Rodar de novo é inofensivo: as tabelas apenas ficam vazias de novo.

\set ON_ERROR_STOP on
\echo '=> Limpando dados fictícios da Retirada Netwise. Gestor que fica:' :gestor_email

-- Checagem antes de abrir transação: se o gestor não existe, aborta sem mexer em nada.
SET search_path = retirada, public;
SELECT EXISTS (SELECT 1 FROM usuario WHERE email = :'gestor_email') AS gestor_existe \gset
\if :gestor_existe
  \echo '=> Gestor encontrado. Seguindo.'
\else
  \echo ''
  \warn 'ERRO: gestor com e-mail' :gestor_email 'não existe no banco.'
  \warn 'Cadastre-o primeiro (fluxo de convite ou INSERT direto) e rode de novo.'
  \warn 'Nada foi apagado.'
  \quit
\endif

BEGIN;

SET search_path = retirada, public;

-- 1. Garante que o gestor está como admin ativo (sem vínculo CLT/terceiro, sem aprovador, sem parceiro).
UPDATE usuario
   SET papel                = 'admin',
       situacao             = 'ativo',
       vinculo              = NULL,
       aprovado_por         = NULL,
       empresa_parceira_id  = NULL,
       atualizado_em        = now()
 WHERE email = :'gestor_email';

-- 2. Solta FKs que, se deixadas de pé, fariam o TRUNCATE CASCADE abaixo apagar o próprio gestor
--    (CASCADE propaga em cadeia; `usuario` referencia `empresa_parceira`, e truncar `empresa_parceira`
--    sem cortar essa FK derrubaria também a linha do Pedro).
UPDATE usuario
   SET empresa_parceira_id = NULL
 WHERE empresa_parceira_id IS NOT NULL;

UPDATE parametro
   SET atualizado_por = NULL
 WHERE atualizado_por IS NOT NULL
   AND atualizado_por <> (SELECT id FROM usuario WHERE email = :'gestor_email');

-- 3. Zera toda a parte operacional.
--    `empresa_parceira` fica fora do TRUNCATE porque é PAI de `usuario.empresa_parceira_id`:
--    `TRUNCATE empresa_parceira CASCADE` arrastaria também a linha do gestor Pedro, por estrutura
--    (CASCADE olha o schema das FKs, não os valores). Como já zeramos todas as empresa_parceira_id
--    no passo 2, um DELETE sem conflito dá conta.
TRUNCATE TABLE
    ordem,
    cliente,
    telefone_cliente,
    equipamento_ordem,
    evento_ordem,
    evidencia,
    importacao,
    pendencia_importacao,
    tentativa_contato,
    confirmacao_telefone,
    custodia,
    fechamento_parceiro,
    reversao,
    posicao_tecnico,
    repasse_credito,
    repasse_pagamento,
    resgate,
    chave_pix,
    auditoria_acesso,
    convite_usuario
    RESTART IDENTITY CASCADE;

DELETE FROM empresa_parceira;

-- 4. Chaves de acesso: mantém só as do gestor Pedro.
DELETE FROM chave_de_acesso
 WHERE usuario_id <> (SELECT id FROM usuario WHERE email = :'gestor_email');

-- 5. Qualquer outro usuário (técnico demo, financeiro demo, terceiro demo, etc.) sai.
DELETE FROM usuario
 WHERE email <> :'gestor_email';

-- 6. Mostra o que ficou para conferência imediata.
\echo
\echo '=> Estado depois da limpeza:'
SELECT 'usuarios'          AS tabela, count(*) AS linhas FROM usuario
UNION ALL SELECT 'chaves_acesso',       count(*) FROM chave_de_acesso
UNION ALL SELECT 'convites',            count(*) FROM convite_usuario
UNION ALL SELECT 'clientes',            count(*) FROM cliente
UNION ALL SELECT 'telefones_cliente',   count(*) FROM telefone_cliente
UNION ALL SELECT 'ordens',              count(*) FROM ordem
UNION ALL SELECT 'equipamentos',        count(*) FROM equipamento_ordem
UNION ALL SELECT 'eventos_ordem',       count(*) FROM evento_ordem
UNION ALL SELECT 'evidencias',          count(*) FROM evidencia
UNION ALL SELECT 'importacoes',         count(*) FROM importacao
UNION ALL SELECT 'tentativas_contato',  count(*) FROM tentativa_contato
UNION ALL SELECT 'empresas_parceiras',  count(*) FROM empresa_parceira
UNION ALL SELECT 'repasse_credito',     count(*) FROM repasse_credito
UNION ALL SELECT 'repasse_pagamento',   count(*) FROM repasse_pagamento
UNION ALL SELECT 'resgate',             count(*) FROM resgate
UNION ALL SELECT 'chave_pix',           count(*) FROM chave_pix
UNION ALL SELECT 'auditoria_acesso',    count(*) FROM auditoria_acesso
ORDER BY tabela;

\echo
\echo '=> Gestor que permaneceu:'
SELECT id, nome, email, papel, situacao, vinculo FROM usuario;

COMMIT;

\echo '=> Limpeza concluída.'
