# ADR-007 · Estratégia de testes

**Status:** Aceita

## Contexto

O RNF10 pede testes automáticos de todas as regras; o sistema mexe com cobrança e com dado pessoal, e uma pessoa precisa confiar no que entrega.

## Decisão

| Tipo | Ferramenta | Quando roda |
| --- | --- | --- |
| Unidade | xUnit; Vitest | Todo PR |
| Integração com o banco | xUnit com PostgreSQL no CI | Todo PR |
| Arquitetura | NetArchTest | Todo PR |
| Contrato da API | OpenAPI comparado com `Contrato/openapi.v1.json` | Todo PR |
| Ponta a ponta, visual e acessibilidade | Playwright e axe | Todo PR |
| Segurança do código | CodeQL (sem a aba Security: o job falha se houver alerta e anexa o relatório); Dependabot | Todo PR e semanal |
| Segurança da aplicação | OWASP ZAP | Antes de cada versão |
| Carga e estresse | k6 (`testes-carga/`) | Antes de cada versão |
| Mutação | Stryker.NET no domínio | Antes de cada versão |

Metas: domínio com escore de mutação de pelo menos 80% (o build quebra abaixo); API com p95 abaixo de 500 ms na lista e 300 ms no detalhe, e menos de 1% de erro na carga (sobra tempo para a tela abrir em 2 s, RNF01).

## Alternativas descartadas

- Só testes de ponta a ponta: lentos e não dizem onde está o erro.
- Cobertura de linhas como meta única: passa com teste que não confere nada; a mutação mede se o teste pega erro.

## Consequências

- Definição de pronto no modelo de PR.
- O primeiro Stryker deu 64% e mostrou guardas sem teste; depois dos testes novos, 97%.
