#!/bin/sh
# Limpeza diária do servidor da Retirada. Chamado pelo systemd timer (deploy/systemd/).
# Mantém o disco com folga para o próximo deploy baixar as imagens novas sem travar.
#
# Roda como root (precisa para o apt e o journalctl), mas chama os comandos docker
# como o usuário cladmin, que é dono do ~/.docker e das imagens.
set -eu

usuario="${RETIRADA_USUARIO:-cladmin}"
pasta_repo="${RETIRADA_PASTA:-/home/$usuario/retirada}"
manter_envbak="${RETIRADA_MANTER_ENVBAK:-3}"

log() {
  printf '[%s] %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$1"
}

disco() {
  df -h --output=pcent,avail /  | tail -n 1 | tr -s ' '
}

log "Disco antes: $(disco)"

# 1. Imagens Docker não referenciadas (versões antigas, dangling e cache de build).
#    O atualizar.sh já faz 'docker image prune -f' no fim de cada deploy, mas só pega
#    dangling; aqui a gente pega as não-usadas e o cache do buildx também.
log "Apagando imagens e cache de build do Docker como '$usuario'..."
runuser -l "$usuario" -c 'docker image prune -af' || log "WARN: docker image prune falhou"
runuser -l "$usuario" -c 'docker builder prune -af' || log "WARN: docker builder prune falhou"

# 2. Backups antigos do .env que o workflow de deploy cria.
if [ -d "$pasta_repo" ]; then
  log "Mantendo os $manter_envbak backups do .env mais recentes em $pasta_repo..."
  # shellcheck disable=SC2012  # ls é previsível aqui (nomes sem espaços)
  ls -1t "$pasta_repo"/.env.bak.* 2>/dev/null | tail -n +"$((manter_envbak + 1))" | xargs -r rm -f
else
  log "WARN: $pasta_repo não existe; pulando .env.bak."
fi

# 3. Cache do apt (pacotes .deb que já foram instalados).
log "Limpando cache do apt..."
apt-get clean

# 4. Logs antigos do journal (mantém 7 dias).
log "Comprimindo logs do journal mais antigos que 7 dias..."
journalctl --vacuum-time=7d >/dev/null 2>&1 || log "WARN: journalctl --vacuum-time falhou"

log "Disco depois: $(disco)"
log "Limpeza concluída."
