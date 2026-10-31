#!/usr/bin/env bash
# Roda na sua máquina o mesmo que o CI, antes de abrir o PR.
#
#   ./testar.sh            tudo: backend (com banco, se RETIRADA_TESTE_BANCO estiver definido), frontend, e2e e vulnerabilidades
#   ./testar.sh rapido     sem e2e e sem banco: para conferir no meio do trabalho
#   ./testar.sh backend    só o backend
#   ./testar.sh frontend   só o frontend
#
# RETIRADA_TESTE_BANCO aponta para um banco DE TESTE com PostGIS (os testes gravam nele). Sem a variável, os testes
# de banco ficam ignorados, como avisa a saída. CHROMIUM_PATH troca o navegador do Playwright, se precisar.
set -euo pipefail
cd "$(dirname "$0")"

modo="${1:-tudo}"
case "$modo" in
  tudo | rapido | backend | frontend) ;;
  *) echo "Uso: ./testar.sh [tudo|rapido|backend|frontend]" >&2; exit 2 ;;
esac

inicio=$SECONDS
etapa() { printf '\n\033[1;34m▶ %s\033[0m\n' "$1"; }
ok() { printf '\033[1;32m✔ %s\033[0m\n' "$1"; }
falha() { printf '\033[1;31m✘ %s\033[0m\n' "$1" >&2; exit 1; }

testar_backend() {
  command -v dotnet >/dev/null || falha "dotnet não encontrado: instale o .NET 10 SDK (docs/ambiente.md)."
  etapa "Backend: compilar"
  (cd backend && dotnet build -c Release -v quiet -nologo)

  etapa "Backend: testes (unidade, arquitetura, contrato e, com banco, integração)"
  if [ "$modo" = "rapido" ]; then
    (cd backend && env -u RETIRADA_TESTE_BANCO dotnet test --no-build -c Release -v quiet -nologo)
  else
    [ -n "${RETIRADA_TESTE_BANCO:-}" ] || echo "Aviso: sem RETIRADA_TESTE_BANCO, os testes de banco ficam ignorados (o CI roda todos)."
    (cd backend && dotnet test --no-build -c Release -v quiet -nologo)
  fi

  etapa "Backend: dependências com falha conhecida (alta ou crítica)"
  local relatorio
  relatorio=$(cd backend && dotnet list package --vulnerable --include-transitive 2>&1)
  if grep -qE '\b(High|Critical)\b' <<<"$relatorio"; then
    echo "$relatorio"
    falha "Há pacote NuGet com falha alta ou crítica. Atualize a versão (ou espere o PR do Dependabot)."
  fi
  ok "Backend sem falha alta ou crítica conhecida"
}

testar_frontend() {
  command -v npm >/dev/null || falha "npm não encontrado: instale o Node 22 (docs/ambiente.md)."
  cd frontend
  if [ ! -d node_modules ]; then
    etapa "Frontend: instalar dependências"
    npm ci
  fi
  etapa "Frontend: tipos";  npm run typecheck --silent
  etapa "Frontend: lint";   npm run lint --silent
  etapa "Frontend: Vitest"; npm test --silent
  etapa "Frontend: dependências com falha conhecida (alta ou crítica, só as que vão para o app)"
  npm audit --omit=dev --audit-level=high
  if [ "$modo" != "rapido" ]; then
    etapa "Frontend: telas (Playwright em 390, 820 e 1280 px, com axe)"
    npm run test:e2e --silent
  fi
  cd ..
}

case "$modo" in
  backend) testar_backend ;;
  frontend) testar_frontend ;;
  *) testar_backend; testar_frontend ;;
esac

ok "Tudo certo ($modo) em $((SECONDS - inicio)) s. Pode abrir o PR."
