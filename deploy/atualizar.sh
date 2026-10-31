#!/bin/sh
# Sobe ou atualiza a produção na versão do .env (VERSAO=vX.Y.Z). ADR-006: produção só recebe versão com tag.
# O código do servidor (compose, Caddyfile, scripts) vai para a MESMA tag das imagens, então os dois andam juntos.
# Rode na pasta do repositório, no servidor:  sh deploy/atualizar.sh
set -eu
cd "$(dirname "$0")/.."

# Tudo dentro de uma função: o "git checkout" troca este arquivo pela versão da tag, e o shell já leu o corpo inteiro.
principal() {
  versao=$(sed -n 's/^VERSAO=//p' .env | tail -n 1)
  case "$versao" in
    v[0-9]*.[0-9]*.[0-9]*) ;;
    *)
      echo "VERSAO=\"$versao\" no .env: a produção só recebe versão com tag, como VERSAO=v0.1.0." >&2
      echo "Veja \"Lançar uma versão\" em docs/implantacao.md." >&2
      exit 1 ;;
  esac

  git fetch --quiet --tags --force origin
  if ! git rev-parse --quiet --verify "refs/tags/$versao" >/dev/null; then
    echo "A tag $versao não existe no GitHub. Crie a versão antes (docs/implantacao.md, \"Lançar uma versão\")." >&2
    exit 1
  fi
  git -c advice.detachedHead=false checkout --quiet "$versao"
  echo "Código e imagens na versão $versao ($(git rev-parse --short HEAD))."

  compose="docker compose -f docker-compose.yml -f docker-compose.producao.yml"

  $compose pull                      # imagens da API e do app nessa versão (GHCR), banco, fotos e Caddy
  $compose up -d --no-build --remove-orphans
  docker image prune -f              # disco pequeno: apaga as imagens antigas que ficaram sem uso
  $compose ps
}

principal "$@"; exit $?
