-- Repasse ao técnico (CLT ou PJ, o mesmo valor): cada retirada CONFERIDA pelo estoque gera um crédito para quem
-- retirou. O financeiro registra o pagamento do que está em aberto. O valor fica gravado no crédito, então mudar
-- o valor por retirada vale só daqui para a frente. "bonus" e "ajuste" ficam reservados para depois.
SET search_path = retirada, public;

CREATE TABLE repasse_pagamento (
    id              uuid PRIMARY KEY,
    tecnico_id      uuid        NOT NULL REFERENCES usuario (id),
    valor_centavos  integer     NOT NULL CHECK (valor_centavos > 0),
    quantidade      integer     NOT NULL CHECK (quantidade > 0),   -- créditos pagos de uma vez
    observacao      text        CHECK (char_length(observacao) <= 200),
    pago_em         timestamptz NOT NULL,
    registrado_por  uuid        NOT NULL REFERENCES usuario (id)
);
CREATE INDEX repasse_pagamento_tecnico_idx ON repasse_pagamento (tecnico_id, pago_em DESC);

CREATE TABLE repasse_credito (
    id              uuid PRIMARY KEY,
    tecnico_id      uuid        NOT NULL REFERENCES usuario (id),
    ordem_id        uuid        REFERENCES ordem (id) ON DELETE SET NULL,   -- o expurgo da ordem não apaga o dinheiro
    tipo            text        NOT NULL CHECK (tipo IN ('retirada', 'bonus', 'ajuste')),
    valor_centavos  integer     NOT NULL CHECK (valor_centavos <> 0),
    creditado_em    timestamptz NOT NULL,
    pagamento_id    uuid        REFERENCES repasse_pagamento (id)
);
-- Uma ordem gera um crédito de retirada só (conferir de novo, ou reenviar, não paga duas vezes).
CREATE UNIQUE INDEX repasse_credito_retirada_uq ON repasse_credito (ordem_id) WHERE tipo = 'retirada';
CREATE INDEX repasse_credito_aberto_idx ON repasse_credito (tecnico_id) WHERE pagamento_id IS NULL;

GRANT SELECT, INSERT ON repasse_pagamento TO retirada_app;
GRANT SELECT, INSERT, UPDATE (pagamento_id) ON repasse_credito TO retirada_app;
