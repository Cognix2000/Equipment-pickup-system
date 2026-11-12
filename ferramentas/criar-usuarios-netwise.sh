#!/bin/sh
# Cria os usuários iniciais da Netwise: um por papel (financeiro, estoque e dois técnicos: CLT e terceiro).
# Rode no servidor, na pasta do repositório. A SENHA sai de SENHA_INICIAL; passe pela linha de comando:
#   SENHA_INICIAL='SuaSenhaAqui123!' sh ferramentas/criar-usuarios-netwise.sh
# A senha precisa de pelo menos 12 caracteres e não pode conter o começo do e-mail.
set -eu
cd "$(dirname "$0")/.."

senha=${SENHA_INICIAL:-}
if [ -z "$senha" ]; then
  echo "Informe a senha em SENHA_INICIAL (mínimo 12 caracteres)." >&2
  exit 1
fi
compose=${COMPOSE:-docker compose -f docker-compose.yml -f docker-compose.producao.yml}

criar() {
  nome=$1; email=$2; papel=$3; vinculo=${4:-}
  extras=""
  [ -n "$vinculo" ] && extras="--vinculo $vinculo"
  # shellcheck disable=SC2086
  $compose run --rm migrador criar-usuario --nome "$nome" --email "$email" --papel "$papel" --senha "$senha" $extras
}

criar "Financeiro Netwise"  "pedromendes211205+financeiro@gmail.com" financeiro
criar "Estoque Netwise"     "pedromendes211205+estoque@gmail.com"    estoque
criar "Técnico Netwise CLT" "pedromendes211205+tecnico@gmail.com"    tecnico clt
criar "Técnico Terceiro"    "pedromendes211205+terceiro@gmail.com"   tecnico terceiro

echo "Prontos. Entre com cada e-mail e a senha que você passou."
