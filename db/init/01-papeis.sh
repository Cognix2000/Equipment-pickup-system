#!/bin/sh
# Roda uma vez, quando o volume do banco é criado (docker-entrypoint-initdb.d).
# Cria o usuário de login da API. Ele herda retirada_app, que não tem DELETE (RN12);
# as permissões em tabela vêm da migração ModeloInicial.
set -eu
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
     -v senha_api="$RETIRADA_API_SENHA" <<'SQL'
CREATE ROLE retirada_app NOLOGIN;
CREATE ROLE retirada_api LOGIN PASSWORD :'senha_api' IN ROLE retirada_app;
SQL
