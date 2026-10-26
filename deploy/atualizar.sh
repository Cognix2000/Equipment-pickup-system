#!/bin/sh
# Sobe ou atualiza a produção com as imagens prontas do GHCR (a versão vem de VERSAO no .env).
# Com MODO_DEMONSTRACAO=true no .env, sobe também os usuários de teste e as ordens fictícias.
# Rode na pasta do repositório, no servidor:  sh deploy/atualizar.sh
set -eu
cd "$(dirname "$0")/.."

arquivos="-f docker-compose.yml -f docker-compose.producao.yml"
if grep -qx 'MODO_DEMONSTRACAO=true' .env; then
  arquivos="$arquivos -f docker-compose.demonstracao.yml"
  echo "Modo demonstração: usuários de teste e ordens fictícias (zere antes de usar com dados reais)."
fi
compose="docker compose $arquivos"

git pull --ff-only                 # Caddyfile, compose e scripts do banco na versão do repositório
$compose pull                      # imagens da API e do app (GHCR), banco, fotos e Caddy
$compose up -d --no-build --remove-orphans
docker image prune -f              # disco pequeno: apaga as imagens antigas que ficaram sem uso
$compose ps
