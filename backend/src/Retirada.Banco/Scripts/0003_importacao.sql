-- Importação (RF03–RF05).
--
-- RN02: além do tipo "BLOQUEIO TEMPORARIO", a planilha pode trazer o serviço ainda suspenso
-- (sem rescisão). Os dois viram pendência, não ordem; a ordem nasce quando a rescisão chegar.
ALTER TABLE retirada.pendencia_importacao DROP CONSTRAINT pendencia_importacao_motivo_check;
ALTER TABLE retirada.pendencia_importacao ADD CONSTRAINT pendencia_importacao_motivo_check
    CHECK (motivo IN ('bloqueio_temporario', 'sem_rescisao'));

-- Reimportar não duplica pendência: só uma aberta por chave.
CREATE UNIQUE INDEX pendencia_importacao_aberta_uq ON retirada.pendencia_importacao (chave_importacao)
    WHERE resolvida_em IS NULL;
