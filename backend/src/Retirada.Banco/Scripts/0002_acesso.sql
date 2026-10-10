-- ADR-010: login com senha Argon2id, bloqueio por tentativas, 2FA por aplicativo e convite de primeiro acesso.
SET search_path = retirada, public;

ALTER TABLE usuario
    ADD COLUMN senha_hash            text,          -- formato PHC do Argon2id; NULL até o primeiro acesso
    ADD COLUMN falhas_login          smallint    NOT NULL DEFAULT 0 CHECK (falhas_login >= 0),
    ADD COLUMN bloqueado_ate         timestamptz,   -- RNF03: 5 erros seguidos bloqueiam por 15 min
    ADD COLUMN totp_segredo_cifrado  text,          -- cifrado pela Data Protection da API; nunca em claro
    ADD COLUMN totp_ativado_em       timestamptz,
    ADD COLUMN totp_ultimo_passo     bigint,        -- impede reusar o mesmo código de 6 dígitos
    ADD COLUMN carimbo_seguranca     uuid        NOT NULL DEFAULT gen_random_uuid(),  -- muda = sessões antigas caem (RF02)
    ADD COLUMN ultimo_login_em       timestamptz;

-- O e-mail é sempre gravado em minúsculas pela aplicação; o índice garante isso no banco também.
ALTER TABLE usuario ADD CONSTRAINT usuario_email_minusculo CHECK (email = lower(email));

CREATE TABLE convite_usuario (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    usuario_id  uuid        NOT NULL REFERENCES usuario (id) ON DELETE CASCADE,
    token_hash  char(64)    NOT NULL UNIQUE,   -- SHA-256 do token; o token em claro só existe no link
    expira_em   timestamptz NOT NULL,
    usado_em    timestamptz,
    criado_por  uuid        NOT NULL REFERENCES usuario (id),
    criado_em   timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX convite_usuario_idx ON convite_usuario (usuario_id);

GRANT SELECT, INSERT, UPDATE ON convite_usuario TO retirada_app;
