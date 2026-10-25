#!/bin/sh
# Sobe ou atualiza a produção com as imagens prontas do GHCR (a versão vem de VERSAO no .env).
# Rode na pasta do repositório, no servidor:  deploy/atualizar.sh
set -eu
cd "$(dirname "$0")/.."
compose="docker compose -f docker-compose.yml -f docker-compose.producao.yml"

git pull --ff-only                 # Caddyfile, compose e scripts do banco na versão do repositório
$compose pull                      # imagens da API e do app (GHCR), banco, fotos e Caddy
$compose up -d --no-build --remove-orphans
docker image prune -f              # disco pequeno: apaga as imagens antigas que ficaram sem uso
$compose ps
