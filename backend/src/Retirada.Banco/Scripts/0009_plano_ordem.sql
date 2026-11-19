-- RF03-RF05 (nov/2026): o plano de internet do cliente, extraído da descrição do HubSoft
-- ("PLANO: 600 MB", "PLANO: 1 GB"...). Fica como texto livre normalizado — o técnico só precisa ver
-- para saber que equipamento esperar.
SET search_path = retirada, public;

ALTER TABLE ordem ADD COLUMN plano text;
