#!/bin/sh
# Aplica no GitHub a proteção da main de .github/protecao-main.json (ADR-006). Precisa do gh logado como admin.
#
#     sh .github/proteger-main.sh
#
# Rodar de novo atualiza o ruleset existente em vez de criar outro.
set -eu
cd "$(dirname "$0")"

repo=$(gh repo view --json nameWithOwner -q .nameWithOwner)
nome=$(sed -n 's/^  "name": "\(.*\)",$/\1/p' protecao-main.json)

# Só squash merge, e a branch some depois do merge.
gh api -X PATCH "repos/$repo" --silent \
  -F allow_squash_merge=true -F allow_merge_commit=false -F allow_rebase_merge=false \
  -F delete_branch_on_merge=true

id=$(gh api "repos/$repo/rulesets" -q ".[] | select(.name == \"$nome\") | .id")
if [ -n "$id" ]; then
  gh api -X PUT "repos/$repo/rulesets/$id" --input protecao-main.json --silent
  echo "Ruleset \"$nome\" atualizado em $repo."
else
  gh api -X POST "repos/$repo/rulesets" --input protecao-main.json --silent
  echo "Ruleset \"$nome\" criado em $repo."
fi
