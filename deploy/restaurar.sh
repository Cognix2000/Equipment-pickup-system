#!/bin/sh
# Restaura um backup feito por deploy/backup.sh. APAGA o banco, as fotos e as chaves atuais e põe os do backup.
# Na pasta do repositório, no servidor (o mesmo ou um novo, depois do passo "Primeira vez" de docs/implantacao.md):
#   sh deploy/restaurar.sh backups/retirada-AAAAMMDD-HHMMSS.tar.age ~/chave-backup.txt
# A chave privada do age vem do cofre de senhas só para isto. Apague-a do servidor quando terminar.
# shellcheck disable=SC2016 # as variáveis entre aspas simples expandem dentro do contêiner do banco, de propósito
set -eu
cd "$(dirname "$0")/.."

falhar() { echo "restaurar: $*" >&2; exit 1; }

arquivo=${1:-}
chave=${2:-}
[ -f "$arquivo" ] && [ -f "$chave" ] || falhar "uso: sh deploy/restaurar.sh <backup .tar.age> <chave privada do age>"
command -v age >/dev/null || falhar "instale o age (sudo apt install -y age)."
compose=${COMPOSE:-docker compose -f docker-compose.yml -f docker-compose.producao.yml}
imagem_tar=${IMAGEM_TAR:-alpine:3.22}

umask 077
trabalho=$(mktemp -d)
trap 'rm -rf "$trabalho"' EXIT

echo "1/6 abrindo e conferindo o backup"
age -d -i "$chave" -o "$trabalho/backup.tar" "$arquivo" || falhar "não abriu: a chave não é a deste backup ou o arquivo está corrompido."
tar -C "$trabalho" -xf "$trabalho/backup.tar"
(cd "$trabalho" && sha256sum -c --quiet SHA256SUMS) || falhar "o conteúdo não bate com a conferência (SHA256SUMS)."
sed 's/^/    /' "$trabalho/LEIA-ME.txt"

if [ ! -f .env ]; then
  # Servidor novo: o .env do backup traz as senhas com que o banco e as fotos foram criados.
  cp "$trabalho/env" .env
  echo "    servidor sem .env: usei o .env do backup."
elif ! cmp -s .env "$trabalho/env"; then
  echo "    AVISO: o .env deste servidor é diferente do backup. Fica o daqui; o do backup está dentro do arquivo." >&2
fi

printf '\nIsto APAGA o banco, as fotos e as chaves deste servidor e põe os do backup acima.\nDigite RESTAURAR para seguir: '
read -r resposta
[ "$resposta" = "RESTAURAR" ] || falhar "cancelado, nada mudou."

echo "2/6 parando o sistema"
$compose up --no-start > /dev/null 2>&1 || $compose create > /dev/null
$compose stop proxy web api fotos > /dev/null 2>&1 || true
$compose up -d banco > /dev/null
tentativas=0
until $compose exec -T banco sh -c 'pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB"' > /dev/null 2>&1; do
  tentativas=$((tentativas + 1))
  [ "$tentativas" -lt 60 ] || falhar "o banco não respondeu em 60 s."
  sleep 1
done

echo "3/6 banco"
# O papel do expurgo não vem no dump (papéis são globais); o da API nasce com o volume (db/init).
$compose exec -T banco sh -c 'psql -v ON_ERROR_STOP=1 -q -U "$POSTGRES_USER" -d postgres -c "DO \$\$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '"'"'retirada_expurgo'"'"') THEN CREATE ROLE retirada_expurgo NOLOGIN; END IF; END \$\$;"'
$compose exec -T banco sh -c 'dropdb --force --if-exists -U "$POSTGRES_USER" "$POSTGRES_DB" && createdb -U "$POSTGRES_USER" -T template0 "$POSTGRES_DB"'
$compose exec -T banco sh -c 'pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --exit-on-error' < "$trabalho/banco.dump"

echo "4/6 chaves de sessão e 2FA"
id_api=$($compose ps -aq api)
docker run --rm --volumes-from "$id_api" -v "$trabalho:/entrada:ro" "$imagem_tar" \
  sh -c 'find /chaves -mindepth 1 -delete && tar -C /chaves -xzf /entrada/chaves.tar.gz'

echo "5/6 fotos"
id_fotos=$($compose ps -aq fotos)
docker run --rm --volumes-from "$id_fotos" -v "$trabalho:/entrada:ro" "$imagem_tar" \
  sh -c 'find /data -mindepth 1 -delete && tar -C /data -xzf /entrada/fotos.tar.gz'

echo "6/6 subindo de novo"
$compose up -d --no-build
dominio=$(sed -n 's/^DOMINIO=//p' .env | tail -n 1)
echo "Restaurado. Confira: curl -sI https://$dominio/health"
