-- Resgate do técnico terceiro por PIX (Asaas). O CLT recebe na folha e não resgata.
-- O crédito reservado por um resgate em andamento não entra em outro resgate nem no pagamento manual;
-- se o PIX falhar, a reserva cai e o crédito volta ao saldo.
SET search_path = retirada, public;

-- Vínculo do técnico: decide como ele recebe. O papel continua "tecnico" para os dois.
-- Técnico sem vínculo conta como CLT (o caminho sem resgate); outros papéis não têm vínculo.
ALTER TABLE usuario ADD COLUMN vinculo text CHECK (vinculo IN ('clt', 'terceiro'));
UPDATE usuario SET vinculo = 'clt' WHERE papel = 'tecnico';
ALTER TABLE usuario ADD CONSTRAINT usuario_vinculo_so_de_tecnico CHECK (vinculo IS NULL OR papel = 'tecnico');

-- Chave PIX do técnico. Ela só vale depois que o financeiro aprova: é a defesa contra quem entra na conta
-- do técnico e troca a chave para a dele.
CREATE TABLE chave_pix (
    id              uuid PRIMARY KEY,
    tecnico_id      uuid        NOT NULL REFERENCES usuario (id),
    tipo            text        NOT NULL CHECK (tipo IN ('cpf', 'cnpj', 'email', 'telefone', 'aleatoria')),
    chave           text        NOT NULL CHECK (char_length(chave) BETWEEN 3 AND 77),
    situacao        text        NOT NULL CHECK (situacao IN ('pendente', 'aprovada', 'recusada', 'substituida')),
    cadastrada_em   timestamptz NOT NULL,
    decidida_em     timestamptz,
    decidida_por    uuid        REFERENCES usuario (id),
    motivo_recusa   text        CHECK (char_length(motivo_recusa) <= 200)
);
CREATE UNIQUE INDEX chave_pix_aprovada_uq ON chave_pix (tecnico_id) WHERE situacao = 'aprovada';
CREATE UNIQUE INDEX chave_pix_pendente_uq ON chave_pix (tecnico_id) WHERE situacao = 'pendente';

CREATE TABLE resgate (
    id               uuid PRIMARY KEY,
    tecnico_id       uuid        NOT NULL REFERENCES usuario (id),
    chave_pix_id     uuid        NOT NULL REFERENCES chave_pix (id),
    valor_centavos   integer     NOT NULL CHECK (valor_centavos > 0),
    quantidade       integer     NOT NULL CHECK (quantidade > 0),
    situacao         text        NOT NULL CHECK (situacao IN ('solicitado', 'enviado', 'pago', 'falhou')),
    solicitado_em    timestamptz NOT NULL,
    gateway_id       text        UNIQUE,
    id_fim_a_fim     text        CHECK (char_length(id_fim_a_fim) <= 64),   -- o ID do Banco Central que prova o PIX
    comprovante_url  text        CHECK (char_length(comprovante_url) <= 500),
    motivo_falha     text        CHECK (char_length(motivo_falha) <= 300),
    concluido_em     timestamptz,
    pagamento_id     uuid        REFERENCES repasse_pagamento (id)
);
-- Um resgate em andamento por técnico: clique duplo não manda dois PIX.
CREATE UNIQUE INDEX resgate_em_andamento_uq ON resgate (tecnico_id) WHERE situacao IN ('solicitado', 'enviado');
CREATE INDEX resgate_tecnico_idx ON resgate (tecnico_id, solicitado_em DESC);

ALTER TABLE repasse_credito ADD COLUMN resgate_id uuid REFERENCES resgate (id);
CREATE INDEX repasse_credito_livre_idx ON repasse_credito (tecnico_id) WHERE pagamento_id IS NULL AND resgate_id IS NULL;

GRANT UPDATE (vinculo) ON usuario TO retirada_app;
GRANT SELECT, INSERT, UPDATE (situacao, decidida_em, decidida_por, motivo_recusa) ON chave_pix TO retirada_app;
GRANT SELECT, INSERT, UPDATE (situacao, gateway_id, id_fim_a_fim, comprovante_url, motivo_falha, concluido_em, pagamento_id) ON resgate TO retirada_app;
GRANT UPDATE (resgate_id) ON repasse_credito TO retirada_app;
