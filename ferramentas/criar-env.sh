#!/bin/sh
# Cria o .env com senhas aleatórias fortes. Ele substitui os antigos arquivos de exemplo, que não ficam mais no Git.
#
#   sh ferramentas/criar-env.sh homologacao                            # na sua máquina (docker compose up)
#   sh ferramentas/criar-env.sh producao pedro.devioslab.com.br v0.1.0  # no servidor: domínio e versão lançada
#
# Nunca sobrescreve um .env que já existe: as senhas do banco ficam gravadas no volume na primeira subida,
# e trocá-las depois impede a API de entrar no banco. Guarde uma cópia do .env de produção num cofre de senhas.
set -eu
cd "$(dirname "$0")/.."

ambiente="${1:-}"
if [ -e .env ]; then
  echo "Já existe um .env aqui. Ele não foi mudado (apague-o à mão só se tiver certeza)." >&2
  exit 1
fi
command -v openssl >/dev/null || { echo "Instale o openssl para gerar as senhas." >&2; exit 1; }
segredo() { openssl rand -hex 24; }

umask 077 # o .env nasce legível só pelo dono
case "$ambiente" in
  homologacao)
    cat > .env <<EOF
# Homologação na sua máquina. Gerado por ferramentas/criar-env.sh; nunca vai para o Git.
BANCO_SENHA_DONO=$(segredo)
BANCO_SENHA_API=$(segredo)
# RN14: armazenamento das fotos (SeaweedFS), só dentro do Docker.
FOTOS_CHAVE=$(segredo)
FOTOS_SEGREDO=$(segredo)
# Porta do app na sua máquina (mude se a 8080 estiver ocupada).
WEB_PORTA=8080
# RN05: aceita os telefones fictícios com DDD 00.
IMPORTACAO_MODO_SIMULACAO=true
# ADR-008: vazio = sem telemetria. Com o perfil observabilidade: http://observabilidade:4317
OTEL_ENDPOINT=
EOF
    ;;
  producao)
    dominio="${2:-}"
    versao="${3:-}"
    case "$versao" in v[0-9]*.[0-9]*.[0-9]*) ;; *) versao="" ;; esac
    if [ -z "$dominio" ] || [ -z "$versao" ]; then
      echo "Uso: sh ferramentas/criar-env.sh producao <domínio> <versão, ex.: v0.1.0>" >&2
      exit 2
    fi
    cat > .env <<EOF
# Produção. Gerado por ferramentas/criar-env.sh; nunca vai para o Git. Guarde uma cópia num cofre de senhas.
# Versão em produção: sempre uma tag lançada (ADR-006). Trocar a versão: docs/implantacao.md, "Lançar uma versão".
VERSAO=$versao
# Domínio com registro DNS tipo A para este servidor; o HTTPS sai para ele.
DOMINIO=$dominio
BANCO_SENHA_DONO=$(segredo)
BANCO_SENHA_API=$(segredo)
FOTOS_CHAVE=$(segredo)
FOTOS_SEGREDO=$(segredo)
# O app só sai pelo Caddy (HTTPS); a 8080 escuta só no próprio servidor, para diagnóstico.
WEB_PORTA=127.0.0.1:8080
IMPORTACAO_MODO_SIMULACAO=false
OTEL_ENDPOINT=
# Repasse ao técnico por retirada conferida, em centavos (500 = R$ 5,00).
VALOR_POR_RETIRADA_CENTAVOS=500
# Backup automático (deploy/backup.sh; docs/implantacao.md, "Backup automático").
# Chave PÚBLICA do age (age1...). A privada fica com você, num cofre de senhas, nunca neste servidor.
BACKUP_CHAVE_PUBLICA=
# Destino do rclone para a cópia fora do servidor (ex.: nuvem:retirada-backup). Vazio = só neste servidor.
BACKUP_DESTINO=
BACKUP_DIAS=14
BACKUP_DIAS_REMOTO=30
# Endereço chamado quando o backup termina bem (monitor de batimento). Vazio = não chama.
BACKUP_AVISO_URL=
EOF
    ;;
  *)
    echo "Uso: sh ferramentas/criar-env.sh homologacao | producao <domínio> <versão>" >&2
    exit 2 ;;
esac

echo ".env criado ($ambiente), com senhas aleatórias e permissão só para o dono."
