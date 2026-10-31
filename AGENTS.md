# AGENTS.md · Retirada Netwise

Instruções para agentes de IA (Claude, Codex, Cursor, Copilot, Gemini e outros) **antes de executar qualquer ação** neste repositório. Uma pessoa nova no projeto também deve começar por aqui.

## O que é

Sistema da Netwise, provedor de internet FTTH, para **retirar os equipamentos de clientes inadimplentes**. Ele substitui a planilha do financeiro: importa as ordens do HubSoft, o gestor designa ao técnico, o técnico recolhe com GPS e fotos, o estoque confere e o financeiro dá a baixa e paga o repasse do técnico.

Stack:
- backend em .NET 10 (monólito modular, minimal APIs, EF Core);
- PostgreSQL com PostGIS;
- PWA em React 19.

## Leia antes de agir, nesta ordem

1. **[CLAUDE.md](CLAUDE.md):** as regras que não se negociam, os padrões do código e as armadilhas conhecidas. Vale para qualquer agente, não só o Claude.
2. **[docs/README.md](docs/README.md):** o mapa da documentação e onde está cada coisa.
3. **[docs/arquitetura.md](docs/arquitetura.md)** e **[docs/modelo-de-dados.md](docs/modelo-de-dados.md):** como as partes se ligam e o que cada tabela guarda.
4. **[docs/adr](docs/adr/README.md):** o porquê de cada decisão técnica. Não reverta uma decisão sem uma ADR nova.
5. **[CONTRIBUTING.md](CONTRIBUTING.md):** branch, commit, PR e definição de pronto.

## Regras que valem para todo agente

- **Só dados fictícios:**
  - telefones com DDD `00`;
  - e-mails `@exemplo.invalid`;
  - séries `SIMU…`/`HWTC…`;
  - "Cidade Simulada".

  Nunca use dado real de cliente nem nome de pessoa da empresa, em código, teste, log, issue ou captura.
- **LGPD:** não grave CPF/CNPJ, e-mail do cliente, plano nem PPPoE. O histórico mostra o papel de quem fez, nunca o nome.
- **Nunca commite direto na `main`.** Use uma branch curta, Conventional Commits em português e um PR com o CI verde.
- **`git add` só com caminhos explícitos**, nunca `git add -A` (as capturas locais do Playwright não vão para o commit).
- **Nada de segredos no repositório:** `.env`, chaves e senhas ficam fora. Não peça ao Pedro para colar token ou senha.
- **As regras de negócio detalhadas ficam fora do repositório**, no documento do projeto. Aqui ficam os códigos (RN01…RN16) e o comportamento implementado.
- **Mudança grande** (esquema, contrato da API, autenticação, dependência nova): explique o impacto e peça confirmação antes.
- **Não comece funcionalidade nova sem pedido.** Na dúvida sobre uma regra, pergunte.

## Antes de dizer que terminou

```bash
./testar.sh            # o mesmo que o CI: backend, frontend e e2e
./testar.sh rapido     # sem e2e e sem banco, para conferir no meio do trabalho
```

Mudou a API de propósito? Atualize o contrato com `ATUALIZAR_CONTRATO=1 dotnet test tests/Retirada.Arquitetura.Testes` e versione `Contrato/openapi.v1.json`.
