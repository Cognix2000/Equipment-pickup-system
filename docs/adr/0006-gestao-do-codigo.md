# ADR-006 · Gestão do código no GitHub

**Status:** Aceita

## Contexto

Uma pessoa desenvolvendo agora, mais gente depois, entrega contínua em homologação e um kanban para acompanhar o trabalho.

## Decisão

GitHub Flow:

| Item | Regra |
| --- | --- |
| `main` | Protegida: só entra por PR com o CI verde. Ruleset sem bypass, nem para o admin, versionado em `.github/protecao-main.json` e aplicado com `sh .github/proteger-main.sh` |
| Branches | Curtas, com tipo e requisito: `feat/rf12-registrar-retirada`, `fix/importacao-telefone` |
| Commits e título do PR | Conventional Commits em português: `feat(ordens): registrar retirada (RF12)` |
| Merge | Squash: um commit por PR |
| Revisão | Checklist do PR até haver segunda pessoa; depois, uma aprovação obrigatória |
| Versões | SemVer por incremento (`v0.1.0` no incremento 1) |
| Ambientes | Merge na `main` vai para homologação; tag vai para produção, com aprovação manual |

Kanban no GitHub Projects: Backlog → Pronto para começar → Em andamento → Em revisão → Em homologação → Concluído, com os campos Incremento, Módulo, Tipo e Prioridade.

## Alternativas descartadas

- GitFlow: as branches `develop` e `release` dobram os merges sem ganho para entrega contínua.
- Commit direto na `main`: sem CI antes, um erro chega em homologação.

## Consequências

- O histórico da `main` vira o changelog.
- Modelos de issue e de PR, rótulos e CODEOWNERS ficam em `.github/`; o CI recusa título de PR fora do padrão.
