-- Localização do técnico em tempo real: só a ÚLTIMA posição de cada um, sem trajeto (decisão do Pedro, menos dado
-- pessoal pela LGPD). O celular manda enquanto o app está aberto; o gestor acompanha no mapa da equipe.
SET search_path = retirada, public;

CREATE TABLE posicao_tecnico (
    tecnico_id     uuid PRIMARY KEY REFERENCES usuario (id) ON DELETE CASCADE,  -- uma linha por técnico
    local          geography(Point, 4326) NOT NULL,
    precisao_m     real CHECK (precisao_m >= 0),
    registrada_em  timestamptz NOT NULL,                -- relógio do aparelho
    recebida_em    timestamptz NOT NULL DEFAULT now()   -- relógio do servidor
);

GRANT SELECT, INSERT, UPDATE ON posicao_tecnico TO retirada_app;
