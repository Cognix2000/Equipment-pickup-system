-- Inscrições de Web Push (VAPID). Uma por usuário+dispositivo; a inscrição substitui a anterior
-- do mesmo endpoint (o navegador pode rotacionar as chaves, mas o endpoint fica estável enquanto
-- o app está instalado). O técnico e os outros papéis podem receber notificações push.
SET search_path = retirada, public;

CREATE TABLE push_inscricao (
    id              uuid        PRIMARY KEY,
    usuario_id      uuid        NOT NULL REFERENCES usuario (id),
    endpoint        text        NOT NULL CHECK (char_length(endpoint) BETWEEN 10 AND 2048),
    -- As duas chaves vêm do PushSubscription.getKey() do navegador (base64url, sem padding).
    chave_p256dh    text        NOT NULL CHECK (char_length(chave_p256dh) BETWEEN 10 AND 200),
    chave_auth      text        NOT NULL CHECK (char_length(chave_auth) BETWEEN 10 AND 100),
    agente          text        CHECK (char_length(agente) <= 200),   -- User-Agent resumido para diagnóstico
    inscrita_em     timestamptz NOT NULL,
    atualizada_em   timestamptz NOT NULL
);

-- Um endpoint é único no sistema: mesmo endpoint → atualiza as chaves e o usuário.
CREATE UNIQUE INDEX push_inscricao_endpoint_uq ON push_inscricao (endpoint);
-- Busca rápida das inscrições de um usuário para disparar notificações.
CREATE INDEX push_inscricao_usuario_idx ON push_inscricao (usuario_id);

GRANT SELECT, INSERT, UPDATE, DELETE ON push_inscricao TO retirada_app;
