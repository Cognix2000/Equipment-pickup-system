-- RF02 (nov/2026): acrescenta 'apagado' como situação possível do usuário.
-- Usuário apagado é a anonimização que vem depois do revogado: perde dado pessoal e some da lista,
-- mas a linha fica no banco porque o histórico (RN12) aponta para o Id dela.
SET search_path = retirada, public;

ALTER TABLE usuario DROP CONSTRAINT usuario_situacao_check;
ALTER TABLE usuario ADD CONSTRAINT usuario_situacao_check
    CHECK (situacao IN ('pendente', 'ativo', 'suspenso', 'revogado', 'apagado'));
