-- ADR-012: chave de acesso (passkey, WebAuthn) como alternativa à senha e ao aplicativo autenticador.
-- Só a chave PÚBLICA fica aqui; a privada nunca sai do aparelho da pessoa.
SET search_path = retirada, public;

CREATE TABLE chave_de_acesso (
    id             uuid PRIMARY KEY,
    usuario_id     uuid        NOT NULL REFERENCES usuario (id) ON DELETE CASCADE,
    credencial_id  bytea       NOT NULL UNIQUE,     -- identificador que o aparelho manda a cada login
    chave_publica  bytea       NOT NULL,            -- COSE; confere a assinatura do aparelho
    contador       bigint      NOT NULL DEFAULT 0 CHECK (contador >= 0),
    nome           text        NOT NULL CHECK (char_length(nome) BETWEEN 1 AND 60),  -- "Celular", "Notebook"…
    criada_em      timestamptz NOT NULL DEFAULT now(),
    ultimo_uso_em  timestamptz
);
CREATE INDEX chave_de_acesso_usuario_idx ON chave_de_acesso (usuario_id);

-- A pessoa remove a própria chave pela tela "Minha conta"; o gestor remove todas ao zerar o 2FA.
GRANT SELECT, INSERT, UPDATE, DELETE ON chave_de_acesso TO retirada_app;
