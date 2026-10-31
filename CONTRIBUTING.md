# Como contribuir

As regras abaixo vêm do [ADR-006](docs/adr/0006-gestao-do-codigo.md) e do [ADR-007](docs/adr/0007-estrategia-de-testes.md).

## Do kanban até a main

1. Pegue uma issue em **Pronto para começar** no GitHub Projects e mova para **Em andamento**.
2. Crie uma branch curta a partir da `main`: `tipo/nº-da-issue-descrição`. O número liga a branch ao card do kanban; sem issue, use o requisito.

   ```bash
   git switch main && git pull
   git switch -c feat/42-mapa-tempo-real        # issue #42
   git switch -c fix/rf12-serie-com-espaco      # sem issue: o requisito
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

   **Escopos** (o que vai entre parênteses):

   | Escopo | Parte do sistema |
   | --- | --- |
   | `acesso` | Login, 2FA, chave de acesso, usuários |
   | `ordens` | Ordem, retirada, conferência, contato, fotos |
   | `importacao` | Planilha, prévia e gravação |
   | `repasses` | Valor a receber do técnico e pagamento |
   | `implantacao` | Servidor, compose, Caddy, deploy |
   | `deps` | Dependências (o Dependabot usa) |

   Cite a issue no fim do assunto, quando houver: `feat(ordens): mapa com a posição do técnico (#42)`. **Não use emoji**: o CI confere o título do PR pelo padrão.

4. Abra o PR com o título no mesmo padrão (o CI recusa outro formato) e preencha o checklist. Mova a issue para **Em revisão**.
5. Com o CI verde, faça **squash merge**. A `main` vai para homologação; mova a issue para **Em homologação**.
6. Conferido em homologação, mova para **Concluído**.

## Antes de abrir o PR

```bash
./testar.sh          # o mesmo que o CI: backend, frontend, e2e e dependências com falha conhecida
./testar.sh rapido   # sem e2e e sem banco, para conferir no meio do trabalho
```

Os outros comandos estão em [docs/comandos.md](docs/comandos.md).

- **Mudou a API de propósito?** Rode `ATUALIZAR_CONTRATO=1 dotnet test` e versione `Contrato/openapi.v1.json`.
- **Mudou uma tela?** Atualize as capturas de referência e confira a diferença no artefato do CI.
- **Criou uma tabela, um módulo ou um comando?** Atualize a documentação no mesmo PR (veja "Manter atualizado" em [docs/README.md](docs/README.md)).
- **CI vermelho em "Dependências com falha conhecida"?** Um pacote tem falha alta ou crítica: atualize a versão (o Dependabot costuma já ter aberto o PR).

## Dados de cliente

Nunca use dado real em código, teste, issue, log ou captura de tela. Use os fictícios de `db/dados-ficticios` e `ferramentas/dados-ficticios`: DDD 00, e-mails `.invalid` e séries `SIMU`.

## Versões

Ao fechar um incremento, crie a tag `vX.Y.Z` na `main`. Produção só recebe versão com tag, com aprovação manual.
