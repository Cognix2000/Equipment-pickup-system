#!/bin/sh
# Backup da produção num arquivo só, cifrado: banco, fotos (RN14), chaves de sessão e 2FA, e o .env.
# Agendado pelo cron (docs/implantacao.md, "Backup automático"). Na pasta do repositório, no servidor:
#   sh deploy/backup.sh
#
# Precisa no .env:
#   BACKUP_CHAVE_PUBLICA  chave pública do age (age1...). A chave privada NÃO fica no servidor: sem ela, ninguém
#                         abre o backup, nem quem roubar o disco ou a conta da nuvem. Guarde-a num cofre de senhas.
# Opcionais:
#   BACKUP_DESTINO        destino do rclone para a cópia fora do servidor (ex.: nuvem:retirada-backup). Vazio = só local.
#   BACKUP_DIAS           quantos dias de backup ficam no servidor (padrão 14).
#   BACKUP_DIAS_REMOTO    quantos dias ficam no destino remoto (padrão 30).
#   BACKUP_AVISO_URL      endereço chamado quando o backup termina bem (monitor de "batimento"). Vazio = não chama.
# shellcheck disable=SC2016 # as variáveis entre aspas simples expandem dentro do contêiner do banco, de propósito
set -eu
cd "$(dirname "$0")/.."

ler() { sed -n "s/^$1=//p" .env | tail -n 1; }
falhar() { echo "backup: $*" >&2; exit 1; }

[ -f .env ] || falhar "não achei o .env nesta pasta."
chave=$(ler BACKUP_CHAVE_PUBLICA)
destino=$(ler BACKUP_DESTINO)
dias=$(ler BACKUP_DIAS); dias=${dias:-14}
dias_remoto=$(ler BACKUP_DIAS_REMOTO); dias_remoto=${dias_remoto:-30}
aviso=$(ler BACKUP_AVISO_URL)
pasta=${BACKUP_PASTA:-backups}
compose=${COMPOSE:-docker compose -f docker-compose.yml -f docker-compose.producao.yml}
imagem_tar=${IMAGEM_TAR:-alpine:3.22}

case "$chave" in age1*) ;; *) falhar "defina BACKUP_CHAVE_PUBLICA (começa com age1) no .env. Veja docs/implantacao.md, \"Backup automático\"." ;; esac
command -v age >/dev/null || falhar "instale o age (sudo apt install -y age)."
if [ -n "$destino" ]; then command -v rclone >/dev/null || falhar "BACKUP_DESTINO definido, mas falta o rclone (sudo apt install -y rclone)."; fi

umask 077 # o backup tem o .env e os dados: só o dono lê
mkdir -p "$pasta"
# Um backup por vez: se o anterior ainda estiver rodando, este desiste em vez de brigar pelas fotos.
exec 9>"$pasta/.trava"
flock -n 9 || falhar "já existe um backup rodando."

carimbo=$(date -u +%Y%m%d-%H%M%S)
trabalho=$(mktemp -d)
# Aconteça o que acontecer, as fotos voltam a funcionar e a pasta temporária some.
trap '$compose start fotos >/dev/null 2>&1 || true; rm -rf "$trabalho"' EXIT

echo "1/5 banco"
# Formato custom do pg_dump: consistente com a API rodando, e o pg_restore escolhe o que restaurar.
$compose exec -T banco sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc' > "$trabalho/banco.dump"
$compose exec -T banco pg_restore --list < "$trabalho/banco.dump" > /dev/null || falhar "o dump do banco saiu ilegível."

echo "2/5 chaves de sessão e 2FA"
id_api=$($compose ps -aq api)
[ -n "$id_api" ] || falhar "não achei o contêiner da API."
docker run --rm --volumes-from "$id_api:ro" -v "$trabalho:/saida" "$imagem_tar" tar -C /chaves -czf /saida/chaves.tar.gz .

echo "3/5 fotos"
id_fotos=$($compose ps -aq fotos)
[ -n "$id_fotos" ] || falhar "não achei o contêiner das fotos."
# O SeaweedFS para por alguns segundos para a cópia sair inteira. Rode de madrugada: uma foto enviada nesse
# instante falha e fica com "Tentar de novo" no celular do técnico.
$compose stop fotos > /dev/null
docker run --rm --volumes-from "$id_fotos:ro" -v "$trabalho:/saida" "$imagem_tar" tar -C /data -czf /saida/fotos.tar.gz .
$compose start fotos > /dev/null

echo "4/5 .env e conferência"
cp .env "$trabalho/env"
versao=$(ler VERSAO)
{
  echo "Backup da Retirada Netwise"
  echo "feito_em_utc=$carimbo"
  echo "versao=${versao:-desconhecida}"
  echo "commit=$(git rev-parse --short HEAD 2>/dev/null || echo desconhecido)"
  echo "Restaurar: sh deploy/restaurar.sh <este arquivo .age> <chave privada do age>"
} > "$trabalho/LEIA-ME.txt"
(cd "$trabalho" && sha256sum banco.dump chaves.tar.gz fotos.tar.gz env LEIA-ME.txt > SHA256SUMS)

arquivo="$pasta/retirada-$carimbo.tar.age"
tar -C "$trabalho" -cf "$trabalho/backup.tar" banco.dump chaves.tar.gz fotos.tar.gz env LEIA-ME.txt SHA256SUMS
age -r "$chave" -o "$arquivo.parcial" "$trabalho/backup.tar"
mv "$arquivo.parcial" "$arquivo" # só ganha o nome final quando está completo
echo "    $arquivo ($(du -h "$arquivo" | cut -f1))"

echo "5/5 cópia fora do servidor e limpeza"
find "$pasta" -maxdepth 1 -name 'retirada-*.tar.age' -mtime +"$dias" -delete
if [ -n "$destino" ]; then
  rclone copy "$arquivo" "$destino"
  rclone delete --min-age "${dias_remoto}d" --include 'retirada-*.tar.age' "$destino"
  echo "    copiado para $destino"
else
  echo "    AVISO: sem BACKUP_DESTINO, o backup só existe neste servidor. Se o disco morrer, ele vai junto." >&2
fi

if [ -n "$aviso" ]; then curl -fsS -m 10 --retry 3 "$aviso" > /dev/null || echo "    aviso de batimento falhou (o backup está feito)" >&2; fi
echo "Backup pronto."
