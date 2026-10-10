# Como contribuir

As regras abaixo vêm do [ADR-006](docs/adr/0006-gestao-do-codigo.md) e do [ADR-007](docs/adr/0007-estrategia-de-testes.md).

## Do kanban até a main

1. Pegue uma issue em **Pronto para começar** no GitHub Projects e mova para **Em andamento**.
2. Crie uma branch curta a partir da `main`, com o tipo e o requisito:

   ```bash
   git switch main && git pull
   git switch -c feat/rf12-registrar-retirada
   ```

3. Faça commits pequenos no padrão Conventional Commits:

   ```
   feat(ordens): registrar retirada com série conferida (RF12)
   fix(importacao): aceitar telefone com 10 dígitos
   test(ordens): cobrir cancelamento depois da retirada
   ```

   | Tipo | Quando |
   | --- | --- |
   | `feat` | Funcionalidade nova para o usuário |
   | `fix` | Correção de bug |
   | `test` | Só testes |
   | `refactor` | Muda o código sem mudar o comportamento |
   | `docs` | Documentação e ADR |
   | `perf` | Desempenho |
   | `chore`, `ci`, `build` | Manutenção, pipeline, empacotamento |

4. Abra o PR com o título no mesmo padrão (o CI recusa outro formato) e preencha o checklist. Mova a issue para **Em revisão**.
5. Com o CI verde, faça **squash merge**. A `main` vai para homologação; mova a issue para **Em homologação**.
6. Conferido em homologação, mova para **Concluído**.

## Antes de abrir o PR

```bash
cd backend && dotnet test                     # unidade, arquitetura e contrato (banco: veja o README)
cd frontend && npm run typecheck && npm run lint && npm test && npm run test:e2e
```

- **Mudou a API de propósito?** Rode `ATUALIZAR_CONTRATO=1 dotnet test` e versione `Contrato/openapi.v1.json`.
- **Mudou uma tela?** Atualize as capturas de referência e confira a diferença no artefato do CI.

## Dados de cliente

Nunca use dado real em código, teste, issue, log ou captura de tela. Use os fictícios de `db/dados-ficticios` e `ferramentas/dados-ficticios`: DDD 00, e-mails `.invalid` e séries `SIMU`.

## Versões

Ao fechar um incremento, crie a tag `vX.Y.Z` na `main`. Produção só recebe versão com tag, com aprovação manual.
