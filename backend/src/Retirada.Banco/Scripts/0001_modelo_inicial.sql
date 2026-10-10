-- =============================================================================
-- Sistema de Retirada de Equipamentos (Netwise) - modelo de dados, RASCUNHO
-- Aplicado pela migração ModeloInicial (Retirada.Banco). Mudanças novas entram como migrações novas, nunca editando este arquivo.
-- O EF Core mapeia estas tabelas (OrdensDbContext); o esquema é o contrato que o
-- mapeamento deve respeitar.
--
-- PostgreSQL 16 + PostGIS 3. Testado com: psql -v ON_ERROR_STOP=1 -f modelo_de_dados.sql
--
-- Convenções
--   * Datas e horas em timestamptz (UTC no banco; o app mostra no fuso local).
--   * Eventos guardam DUAS horas: a do aparelho (ocorrido_em) e a do servidor
--     (recebido_em). A do aparelho pode estar errada; a do servidor não.
--   * Coordenadas em geography(Point, 4326): [longitude, latitude], como o GeoJSON.
--   * Dinheiro em numeric(12,2). Nunca float.
--   * A aplicação NÃO apaga nada: cancelar é mudar de estado. Só o papel
--     retirada_expurgo tem DELETE, usado pelo job de guarda (ver o fim do arquivo).
--   * Minimização (LGPD art. 6º, III): CPF/CNPJ do cliente, e-mail, nº do contrato,
--     data de ativação, plano, dias em atraso, valor em aberto e login PPPoE são
--     descartados na importação e não têm coluna aqui. O status do serviço só decide,
--     na importação, entre criar a ordem (rescindido) e alertar (ainda suspenso).
--     A tabela importacao registra quais colunas foram descartadas.
--   * Linha de cliente ainda sem rescisão não vira ordem: fica em pendencia_importacao
--     até uma planilha nova trazer a rescisão (a ordem nasce na rescisão).
-- =============================================================================

CREATE EXTENSION IF NOT EXISTS postgis;

CREATE SCHEMA IF NOT EXISTS retirada;
SET search_path = retirada, public;

-- -----------------------------------------------------------------------------
-- Parâmetros configuráveis (cadência, limites, guarda). Valores iniciais = proposta.
-- -----------------------------------------------------------------------------
CREATE TABLE parametro (
    chave           text PRIMARY KEY,
    valor           jsonb       NOT NULL,
    descricao       text        NOT NULL,
    atualizado_por  uuid,                       -- FK adicionada depois de usuario
    atualizado_em   timestamptz NOT NULL DEFAULT now()
);

-- -----------------------------------------------------------------------------
-- Pessoas que usam o sistema
-- -----------------------------------------------------------------------------
-- Empresa parceira (MEI, ME ou LTDA). Cada pessoa dela é um usuario próprio, com login, foto e 2FA.
CREATE TABLE empresa_parceira (
    id                    uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    razao_social          text        NOT NULL,
    documento_cifrado     bytea       NOT NULL, -- CNPJ/CPF da EMPRESA PARCEIRA, cifrado pela aplicação (chave fora do banco)
    contrato_versao       text        NOT NULL, -- versão do contrato com cláusulas de LGPD aceita
    contrato_assinado_em  timestamptz NOT NULL,
    regioes               text[]      NOT NULL DEFAULT '{}',  -- bairros ou cidades que atende
    situacao              text        NOT NULL DEFAULT 'pendente'
                                      CHECK (situacao IN ('pendente', 'ativo', 'suspenso', 'revogado'))
);

CREATE TABLE usuario (
    id                   uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    nome                 text        NOT NULL,
    email                text        NOT NULL UNIQUE,
    telefone_e164        text,
    papel                text        NOT NULL CHECK (papel IN ('admin', 'financeiro', 'tecnico', 'parceiro', 'estoque')),
    situacao             text        NOT NULL DEFAULT 'pendente'
                                     CHECK (situacao IN ('pendente', 'ativo', 'suspenso', 'revogado')),
    empresa_parceira_id  uuid REFERENCES empresa_parceira (id),
    foto_chave           text,       -- foto de identificação que o cliente vê no app (arquivo no S3)
    aprovado_por         uuid REFERENCES usuario (id),
    aprovado_em          timestamptz,
    criado_em            timestamptz NOT NULL DEFAULT now(),
    atualizado_em        timestamptz NOT NULL DEFAULT now(),
    CHECK ((aprovado_por IS NULL) = (aprovado_em IS NULL)),
    CONSTRAINT usuario_parceiro_tem_empresa CHECK ((papel = 'parceiro') = (empresa_parceira_id IS NOT NULL))
    -- 2FA obrigatório para admin, financeiro e parceiro (regra da aplicação, não do banco).
);

ALTER TABLE parametro
    ADD CONSTRAINT parametro_atualizado_por_fk FOREIGN KEY (atualizado_por) REFERENCES usuario (id);

-- -----------------------------------------------------------------------------
-- Cliente: o mínimo para achar a pessoa e falar com ela
-- -----------------------------------------------------------------------------
CREATE TABLE cliente (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    codigo_hubsoft  integer     UNIQUE,  -- código do cliente; NULL até a exportação trazer
    nome            text        NOT NULL,
    tipo_pessoa     char(2)     NOT NULL CHECK (tipo_pessoa IN ('PF', 'PJ')),
    criado_em       timestamptz NOT NULL DEFAULT now(),
    atualizado_em   timestamptz NOT NULL DEFAULT now()
    -- Sem CPF/CNPJ, e-mail, nascimento ou login: a retirada não precisa deles.
);

CREATE TABLE telefone_cliente (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    cliente_id      uuid        NOT NULL REFERENCES cliente (id) ON DELETE CASCADE,
    numero_e164     text        NOT NULL,          -- ex.: +5537912345678 (libphonenumber)
    como_veio       text        NOT NULL,          -- texto original da planilha, para auditoria da limpeza
    tipo            text        NOT NULL DEFAULT 'desconhecido'
                                CHECK (tipo IN ('celular', 'fixo', 'desconhecido')),
    formato_valido  boolean     NOT NULL,
    -- Status vindos do RESULTADO dos contatos, nunca de consulta escondida:
    linha           text        NOT NULL DEFAULT 'nao_verificada'
                                CHECK (linha IN ('nao_verificada', 'atende', 'inexistente')),
    whatsapp        text        NOT NULL DEFAULT 'nao_verificado'
                                CHECK (whatsapp IN ('nao_verificado', 'tem', 'nao_tem', 'nao_entregue')),
    verificado_em   timestamptz,
    UNIQUE (cliente_id, numero_e164)
);

-- -----------------------------------------------------------------------------
-- Importação da planilha do financeiro
-- -----------------------------------------------------------------------------
CREATE TABLE importacao (
    id                   uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    arquivo_nome         text        NOT NULL,
    arquivo_sha256       char(64)    NOT NULL,      -- identifica o arquivo sem guardá-lo
    enviado_por          uuid        NOT NULL REFERENCES usuario (id),
    enviado_em           timestamptz NOT NULL DEFAULT now(),
    situacao             text        NOT NULL DEFAULT 'previa'
                                     CHECK (situacao IN ('previa', 'confirmada', 'descartada')),
    linhas_total         integer     NOT NULL CHECK (linhas_total >= 0),
    linhas_importadas    integer     NOT NULL DEFAULT 0 CHECK (linhas_importadas >= 0),
    linhas_com_erro      integer     NOT NULL DEFAULT 0 CHECK (linhas_com_erro >= 0),
    colunas_descartadas  text[]      NOT NULL DEFAULT '{}',  -- ex.: {"CPF/CNPJ","Login PPPoE","Valor em aberto"}
    relatorio            jsonb       NOT NULL DEFAULT '{}'   -- erros por linha (nº da linha e regra), sem dados pessoais
);

-- -----------------------------------------------------------------------------
-- Ordem de retirada: o centro do modelo
-- -----------------------------------------------------------------------------
CREATE TABLE ordem (
    id                          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    -- Chave da importação: id da OS quando a exportação trouxer; até lá, chave provisória
    -- (hash de nome + endereço + número + data/hora programadas). Ver RN01.
    chave_importacao            text        NOT NULL UNIQUE,
    protocolo                   varchar(20) UNIQUE CHECK (protocolo ~ '^[0-9]{20}$'),
    numero_os                   integer,
    id_ordem_servico_hubsoft    integer UNIQUE,
    tipo_os                     text        NOT NULL,   -- ex.: REMOÇÃO EQUIP FTTH, REMOÇÃO EQUIP INADIMPLENTE
    data_programada             timestamptz,            -- data_inicio_programado + hora_inicio_programado
    observacao                  text,                   -- 15ª coluna da planilha (texto livre)
    id_cliente_servico_hubsoft  integer,
    cliente_id                  uuid        NOT NULL REFERENCES cliente (id),
    importacao_id               uuid REFERENCES importacao (id),

    -- Endereço do SERVIÇO (um cliente pode ter dois serviços em endereços diferentes)
    logradouro                  text        NOT NULL,
    numero                      text,
    complemento                 text,
    bairro                      text        NOT NULL,
    cidade                      text        NOT NULL,
    uf                          char(2)     NOT NULL,
    cep                         char(9),
    referencia                  text,
    localizacao                 geography(Point, 4326),   -- NULL = cliente sem ponto no mapa

    status                      text        NOT NULL DEFAULT 'nova' CHECK (status IN (
                                    'nova',                    -- importada, dentro da carência de 48 h
                                    'disponivel',              -- visível para aceite
                                    'atribuida',
                                    'em_rota',
                                    'concluida',               -- equipamento retirado
                                    'conferida',               -- conferido no estoque (fim do caminho principal)
                                    'nao_concluida',           -- visita sem sucesso; volta para disponivel ou encerra
                                    'cancelada',               -- cliente pagou/renegociou ou serviço voltou a habilitado
                                    'devolvida_na_loja',       -- cliente levou à loja
                                    'encerrada_sem_retirada'   -- limite da cadência; vai para a cobrança
                                )),
    data_rescisao               date,
    aberta_por_excecao          boolean     NOT NULL DEFAULT false,  -- gestor liberou na suspensão total (SÓ se o jurídico aprovar)
    justificativa_excecao       text,
    carencia_ate                timestamptz NOT NULL,  -- importação + 48 h (RN03): o financeiro só envia depois da baixa
    responsavel_id              uuid REFERENCES usuario (id),         -- técnico ou parceiro com a ordem
    agendada_dia                date,
    agendada_turno              text CHECK (agendada_turno IN ('manha', 'tarde')),
    visitas_sem_sucesso         smallint    NOT NULL DEFAULT 0 CHECK (visitas_sem_sucesso >= 0),
    versao                      integer     NOT NULL DEFAULT 1,       -- concorrência otimista: só um aceite vence
    criado_em                   timestamptz NOT NULL DEFAULT now(),
    atualizado_em               timestamptz NOT NULL DEFAULT now(),
    encerrada_em                timestamptz,                          -- base do prazo de guarda
    baixa_hubsoft_em            timestamptz,                          -- financeiro marcou a baixa feita no HubSoft (RF19)
    baixa_hubsoft_por           uuid REFERENCES usuario (id),

    CONSTRAINT ordem_excecao_tem_justificativa CHECK (NOT aberta_por_excecao OR justificativa_excecao IS NOT NULL),
    CONSTRAINT ordem_encerramento_coerente     CHECK ((status IN ('conferida', 'cancelada', 'devolvida_na_loja', 'encerrada_sem_retirada'))
                                                      = (encerrada_em IS NOT NULL))
);

CREATE INDEX ordem_localizacao_gix ON ordem USING gist (localizacao);
CREATE INDEX ordem_ativas_idx      ON ordem (status, bairro) WHERE encerrada_em IS NULL;
CREATE INDEX ordem_cliente_idx     ON ordem (cliente_id);
CREATE INDEX ordem_guarda_idx      ON ordem (encerrada_em) WHERE encerrada_em IS NOT NULL;

-- Aceite único (exemplo do update condicional que a API executa):
--   UPDATE ordem SET status = 'atribuida', responsavel_id = $1, versao = versao + 1
--    WHERE id = $2 AND status = 'disponivel' AND versao = $3;
--   0 linhas afetadas = "ordem já aceita".

-- Linha da planilha que ainda não vira ordem (cliente só suspenso, sem rescisão).
-- Guarda o mínimo; quando uma planilha nova trouxer a rescisão, a importação cria a ordem e fecha a pendência.
CREATE TABLE pendencia_importacao (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    importacao_id   uuid        NOT NULL REFERENCES importacao (id),
    chave_importacao text       NOT NULL,
    codigo_hubsoft  integer,                -- vem quando a exportação trouxer o código do cliente
    motivo          text        NOT NULL CHECK (motivo IN ('bloqueio_temporario')),  -- tipo da OS (RN02)
    criada_em       timestamptz NOT NULL DEFAULT now(),
    resolvida_em    timestamptz,
    ordem_id        uuid REFERENCES ordem (id) ON DELETE SET NULL
);

CREATE UNIQUE INDEX pendencia_aberta_uq ON pendencia_importacao (chave_importacao) WHERE resolvida_em IS NULL;

-- -----------------------------------------------------------------------------
-- Equipamentos da ordem (item a item, pelo nº de série)
-- -----------------------------------------------------------------------------
CREATE TABLE equipamento_ordem (
    id                       uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    ordem_id                 uuid          NOT NULL REFERENCES ordem (id) ON DELETE CASCADE,
    id_produto_item_hubsoft  integer,                 -- patrimônio no HubSoft
    tipo                     text          NOT NULL CHECK (tipo IN ('ONT', 'ONU', 'Roteador', 'Repetidor Mesh', 'Fonte')),
    modelo                   text          NOT NULL,  -- marca e modelo, ex.: Huawei EG8010H
    numero_serie             text,                    -- SN da etiqueta; fonte não tem
    mac                      macaddr,
    valor                    numeric(12,2) CHECK (valor >= 0),  -- valor do termo/OS (cl. 15.1)
    situacao                 text          NOT NULL DEFAULT 'pendente' CHECK (situacao IN (
                                 'pendente', 'retirado', 'devolvido_na_loja', 'conferido', 'divergente', 'extraviado')),
    conferido_por            uuid REFERENCES usuario (id),
    conferido_em             timestamptz,
    UNIQUE (ordem_id, numero_serie),
    CHECK ((conferido_por IS NULL) = (conferido_em IS NULL)),
    CONSTRAINT equipamento_serie_obrigatoria CHECK (tipo = 'Fonte' OR numero_serie IS NOT NULL)
);

CREATE INDEX equipamento_serie_idx ON equipamento_ordem (numero_serie);
-- Um aparelho só pode estar em aberto em UMA ordem por vez (pode voltar em outra ordem no futuro).
CREATE UNIQUE INDEX equipamento_serie_em_aberto_uq ON equipamento_ordem (numero_serie)
    WHERE situacao IN ('pendente', 'retirado', 'divergente');

-- -----------------------------------------------------------------------------
-- Histórico: eventos só de acréscimo, numerados dentro de cada ordem
-- -----------------------------------------------------------------------------
CREATE TABLE evento_ordem (
    id                  bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    ordem_id            uuid        NOT NULL REFERENCES ordem (id) ON DELETE CASCADE,
    seq                 integer     NOT NULL,   -- 1, 2, 3... (a API trava a ordem com FOR UPDATE e soma 1)
    tipo                text        NOT NULL,   -- importada, publicada, aceita, em_rota, retirada, nao_concluida,
                                                -- devolvida_na_loja, cancelada, conferida, confirmacao_telefone...
    status_de           text,
    status_para         text,
    motivo              text,                   -- ex.: CLIENTE AUSENTE, RECUSA
    ator_id             uuid REFERENCES usuario (id),  -- NULL = o próprio sistema
    ator_papel          text,
    ocorrido_em         timestamptz NOT NULL,   -- relógio do aparelho
    recebido_em         timestamptz NOT NULL DEFAULT now(),  -- relógio do servidor
    local               geography(Point, 4326), -- GPS no momento do evento
    precisao_m          real CHECK (precisao_m >= 0),
    aparelho_id         text,
    chave_idempotencia  uuid        NOT NULL UNIQUE,  -- gerada no aparelho: reenviar não duplica
    dados               jsonb       NOT NULL DEFAULT '{}',
    UNIQUE (ordem_id, seq)
);

-- -----------------------------------------------------------------------------
-- Provas: só metadados no banco; o arquivo fica no armazenamento S3
-- -----------------------------------------------------------------------------
CREATE TABLE evidencia (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    ordem_id        uuid        NOT NULL REFERENCES ordem (id) ON DELETE CASCADE,
    evento_id       bigint REFERENCES evento_ordem (id) ON DELETE CASCADE,
    equipamento_id  uuid REFERENCES equipamento_ordem (id) ON DELETE CASCADE,
    tipo            text        NOT NULL CHECK (tipo IN ('equipamento', 'etiqueta_serie', 'comprovante_loja')),
    arquivo_chave   text        NOT NULL UNIQUE,  -- ex.: ordens/<ordem_id>/<id>.jpg
    sha256          char(64)    NOT NULL,         -- prova de que o arquivo não mudou
    bytes           integer     NOT NULL CHECK (bytes > 0),
    mime            text        NOT NULL CHECK (mime IN ('image/jpeg', 'image/webp')),
    largura         smallint,
    altura          smallint,
    capturada_em    timestamptz NOT NULL,         -- relógio do aparelho
    recebida_em     timestamptz NOT NULL DEFAULT now(),
    local           geography(Point, 4326),
    precisao_m      real CHECK (precisao_m >= 0),
    autor_id        uuid        NOT NULL REFERENCES usuario (id)
    -- O arquivo é gravado SEM EXIF: hora e GPS ficam nestas colunas, e nada além.
);

-- -----------------------------------------------------------------------------
-- Cadeia de custódia: quem entregou o equipamento a quem
-- -----------------------------------------------------------------------------
CREATE TABLE custodia (
    id               bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    equipamento_id   uuid        NOT NULL REFERENCES equipamento_ordem (id) ON DELETE CASCADE,
    de_tipo          text        NOT NULL CHECK (de_tipo IN ('cliente', 'usuario', 'loja', 'estoque')),
    de_usuario_id    uuid REFERENCES usuario (id),
    para_tipo        text        NOT NULL CHECK (para_tipo IN ('usuario', 'loja', 'estoque')),
    para_usuario_id  uuid REFERENCES usuario (id),
    em               timestamptz NOT NULL DEFAULT now(),
    evento_id        bigint REFERENCES evento_ordem (id) ON DELETE CASCADE,
    CHECK ((de_tipo = 'usuario') = (de_usuario_id IS NOT NULL)),
    CHECK ((para_tipo = 'usuario') = (para_usuario_id IS NOT NULL))
);

-- -----------------------------------------------------------------------------
-- Contato com o cliente antes da retirada
-- -----------------------------------------------------------------------------
CREATE TABLE tentativa_contato (
    id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    ordem_id     uuid        NOT NULL REFERENCES ordem (id) ON DELETE CASCADE,
    telefone_id  uuid REFERENCES telefone_cliente (id) ON DELETE CASCADE,
    canal        text        NOT NULL CHECK (canal IN ('ligacao', 'whatsapp')),
    modelo       text CHECK (modelo IN ('aviso', 'lembrete', 'confirmacao', 'ausente')),
    resultado    text        NOT NULL CHECK (resultado IN (
                     'atendeu', 'nao_atendeu', 'caixa_postal', 'numero_inexistente',
                     'enviada', 'sem_whatsapp', 'nao_entregue', 'respondeu')),
    feito_por    uuid        NOT NULL REFERENCES usuario (id),
    feito_em     timestamptz NOT NULL DEFAULT now(),
    observacao   text,
    fora_da_janela boolean   NOT NULL DEFAULT false,  -- registro manual fora do horário: fica marcado para o gestor
    CHECK (canal = 'whatsapp' OR modelo IS NULL)
);

CREATE INDEX tentativa_ordem_idx ON tentativa_contato (ordem_id, feito_em);

-- Sem sinal na porta: o financeiro confirma por telefone e dita um código de 6 dígitos.
-- O app só confere o formato; o servidor confere o código na sincronização.
CREATE TABLE confirmacao_telefone (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    ordem_id        uuid        NOT NULL REFERENCES ordem (id) ON DELETE CASCADE,
    solicitada_por  uuid        NOT NULL REFERENCES usuario (id),  -- técnico ou parceiro sem sinal
    registrada_por  uuid        NOT NULL REFERENCES usuario (id),  -- financeiro
    decisao         text        NOT NULL CHECK (decisao IN ('pode_retirar', 'nao_retirar')),
    codigo_hmac     char(64)    NOT NULL,     -- HMAC-SHA256 do código com segredo do servidor; o código não fica no banco
    registrada_em   timestamptz NOT NULL DEFAULT now(),
    valida_ate      timestamptz NOT NULL,     -- parâmetro confirmacao_validade_min
    usada_em        timestamptz,              -- preenchido na sincronização
    evento_id       bigint REFERENCES evento_ordem (id) ON DELETE CASCADE,
    CONSTRAINT confirmacao_validade_futura     CHECK (valida_ate > registrada_em),
    CONSTRAINT confirmacao_pessoas_diferentes  CHECK (solicitada_por <> registrada_por)
);

-- -----------------------------------------------------------------------------
-- Parceiros: reversão (bônus) e fechamento do mês
-- -----------------------------------------------------------------------------
CREATE TABLE reversao (
    id                        uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    ordem_id                  uuid        NOT NULL UNIQUE REFERENCES ordem (id) ON DELETE CASCADE,
    parceiro_id               uuid        NOT NULL REFERENCES usuario (id),
    encaminhada_em            timestamptz NOT NULL DEFAULT now(),
    id_renegociacao_hubsoft   integer,
    efetivada_em              timestamptz,
    primeira_parcela_paga_em  timestamptz,   -- só aqui o bônus é liberado
    situacao                  text        NOT NULL DEFAULT 'encaminhada'
                                          CHECK (situacao IN ('encaminhada', 'efetivada', 'parcela_paga', 'nao_converteu'))
);

CREATE TABLE fechamento_parceiro (
    id                    uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    empresa_parceira_id   uuid          NOT NULL REFERENCES empresa_parceira (id),
    mes                   date          NOT NULL CHECK (mes = date_trunc('month', mes)::date),
    aparelhos_conferidos  integer       NOT NULL CHECK (aparelhos_conferidos >= 0),
    meta_aparelhos        integer CHECK (meta_aparelhos > 0),
    valor_por_aparelho    numeric(12,2) NOT NULL CHECK (valor_por_aparelho >= 0),
    bonus_meta            numeric(12,2) NOT NULL DEFAULT 0 CHECK (bonus_meta >= 0),
    bonus_reversao        numeric(12,2) NOT NULL DEFAULT 0 CHECK (bonus_reversao >= 0),
    descontos             numeric(12,2) NOT NULL DEFAULT 0 CHECK (descontos >= 0),
    saldo_anterior        numeric(12,2) NOT NULL DEFAULT 0 CHECK (saldo_anterior <= 0),  -- negativo trazido do mês anterior
    -- Desconto maior que o ganho do mês não vira cobrança: paga 0 e o resto passa para o mês seguinte.
    a_pagar               numeric(12,2) GENERATED ALWAYS AS (GREATEST(
                              aparelhos_conferidos * valor_por_aparelho + bonus_meta + bonus_reversao - descontos + saldo_anterior, 0)) STORED,
    a_transportar         numeric(12,2) GENERATED ALWAYS AS (LEAST(
                              aparelhos_conferidos * valor_por_aparelho + bonus_meta + bonus_reversao - descontos + saldo_anterior, 0)) STORED,
    situacao              text          NOT NULL DEFAULT 'aberto' CHECK (situacao IN ('aberto', 'aprovado', 'pago')),
    aprovado_por          uuid REFERENCES usuario (id),
    aprovado_em           timestamptz,
    UNIQUE (empresa_parceira_id, mes)
);

-- -----------------------------------------------------------------------------
-- Auditoria de acesso: quem viu ou exportou o quê
-- -----------------------------------------------------------------------------
CREATE TABLE auditoria_acesso (
    id           bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    usuario_id   uuid        NOT NULL REFERENCES usuario (id),
    acao         text        NOT NULL,   -- login, ver_ordem, ver_telefone, exportar, ...
    ordem_id     uuid REFERENCES ordem (id) ON DELETE CASCADE,
    em           timestamptz NOT NULL DEFAULT now(),
    ip           inet,
    aparelho_id  text
);

CREATE INDEX auditoria_usuario_idx ON auditoria_acesso (usuario_id, em);

-- -----------------------------------------------------------------------------
-- Tabelas só de acréscimo: UPDATE é bloqueado para todos
-- -----------------------------------------------------------------------------
CREATE FUNCTION impedir_alteracao() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'A tabela % é só de acréscimo: registre um novo evento em vez de alterar', TG_TABLE_NAME;
END;
$$;

CREATE TRIGGER evento_ordem_imutavel     BEFORE UPDATE ON evento_ordem     FOR EACH ROW EXECUTE FUNCTION impedir_alteracao();
CREATE TRIGGER custodia_imutavel         BEFORE UPDATE ON custodia         FOR EACH ROW EXECUTE FUNCTION impedir_alteracao();
CREATE TRIGGER auditoria_acesso_imutavel BEFORE UPDATE ON auditoria_acesso FOR EACH ROW EXECUTE FUNCTION impedir_alteracao();
CREATE TRIGGER evidencia_imutavel        BEFORE UPDATE ON evidencia        FOR EACH ROW EXECUTE FUNCTION impedir_alteracao();
CREATE TRIGGER tentativa_contato_imutavel BEFORE UPDATE ON tentativa_contato FOR EACH ROW EXECUTE FUNCTION impedir_alteracao();

-- -----------------------------------------------------------------------------
-- Papéis do banco: a aplicação não apaga; só o expurgo apaga
-- -----------------------------------------------------------------------------
DO $$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'retirada_app') THEN
        CREATE ROLE retirada_app NOLOGIN;
    END IF;
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'retirada_expurgo') THEN
        CREATE ROLE retirada_expurgo NOLOGIN;
    END IF;
END;
$$;

GRANT USAGE ON SCHEMA retirada TO retirada_app, retirada_expurgo;
GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA retirada TO retirada_app;
REVOKE UPDATE ON evento_ordem, custodia, auditoria_acesso, evidencia, tentativa_contato FROM retirada_app;
GRANT USAGE ON ALL SEQUENCES IN SCHEMA retirada TO retirada_app;
GRANT SELECT, DELETE ON ordem, cliente, auditoria_acesso, pendencia_importacao TO retirada_expurgo;  -- o resto sai em cascata
GRANT SELECT ON evidencia TO retirada_expurgo;

-- -----------------------------------------------------------------------------
-- Guarda e expurgo (prazo proposto: 5 anos após o encerramento; validar com o jurídico)
-- O job (1x por mês, papel retirada_expurgo) calcula UM corte e usa o mesmo nos dois passos:
--   corte := now() - make_interval(years => <parametro guarda_anos>)
--   1. SELECT arquivo_chave FROM retirada.arquivos_a_expurgar(corte);  -> apaga esses arquivos no S3
--   2. SELECT * FROM retirada.expurgar(corte);                         -> apaga no banco
-- Backups (dump e WAL) giram em <parametro backup_dias>: o dado expurgado some deles quando a cópia vence.
-- -----------------------------------------------------------------------------
CREATE FUNCTION arquivos_a_expurgar(corte timestamptz) RETURNS TABLE (arquivo_chave text)
LANGUAGE sql STABLE AS $$
    SELECT e.arquivo_chave
      FROM retirada.evidencia e
      JOIN retirada.ordem o ON o.id = e.ordem_id
     WHERE o.encerrada_em < corte;
$$;

CREATE FUNCTION expurgar(corte timestamptz)
RETURNS TABLE (ordens integer, clientes integer, auditorias integer, pendencias integer)
LANGUAGE plpgsql AS $$
DECLARE
    n_ordens integer; n_clientes integer; n_auditorias integer; n_pendencias integer;
BEGIN
    DELETE FROM retirada.ordem WHERE encerrada_em < corte;          -- eventos, fotos, contatos... em cascata
    GET DIAGNOSTICS n_ordens = ROW_COUNT;
    DELETE FROM retirada.cliente c                                   -- cliente sem ordem restante (telefones em cascata)
     WHERE c.criado_em < corte
       AND NOT EXISTS (SELECT 1 FROM retirada.ordem o WHERE o.cliente_id = c.id);
    GET DIAGNOSTICS n_clientes = ROW_COUNT;
    DELETE FROM retirada.auditoria_acesso WHERE em < corte;          -- inclui logins sem ordem
    GET DIAGNOSTICS n_auditorias = ROW_COUNT;
    DELETE FROM retirada.pendencia_importacao WHERE criada_em < corte;
    GET DIAGNOSTICS n_pendencias = ROW_COUNT;
    RETURN QUERY SELECT n_ordens, n_clientes, n_auditorias, n_pendencias;
END;
$$;

REVOKE ALL ON FUNCTION arquivos_a_expurgar(timestamptz), expurgar(timestamptz) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION arquivos_a_expurgar(timestamptz), expurgar(timestamptz) TO retirada_expurgo;

-- -----------------------------------------------------------------------------
-- Valores iniciais dos parâmetros (proposta do documento)
-- -----------------------------------------------------------------------------
INSERT INTO parametro (chave, valor, descricao) VALUES
    ('carencia_horas',             '48',  'Prazo para o cliente devolver depois da rescisão (cl. 15.2)'),
    ('limite_visitas',             '3',   'Visitas sem sucesso antes de encerrar sem retirada'),
    ('limite_dias_sem_resposta',   '10',  'Dias após a carência sem resposta antes de encerrar sem retirada'),
    ('horario_contato',            '{"dias": "seg-sab", "inicio": "08:00", "fim": "18:00"}',
                                          'Janela de ligações e mensagens (validar com o jurídico)'),
    ('confirmacao_validade_min',   '120', 'Validade do código de confirmação por telefone'),
    ('prazo_apos_aceite_horas',    '24',  'Sem visita agendada: tempo para sair depois do aceite; vencido, a ordem volta para Disponível'),
    ('backup_dias',                '35',  'Rodízio dos backups; dado expurgado some deles quando a cópia vence'),
    ('prazo_entrega_parceiro',     '"fim do dia útil seguinte"', 'Prazo para o parceiro entregar no estoque'),
    ('guarda_anos',                '5',   'Guarda de ordem, fotos e histórico após o encerramento (CC art. 206, §5º, I; validar)');
