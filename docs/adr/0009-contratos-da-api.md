# ADR-009 · Contratos da API

**Status:** Aceita

## Contexto

O PWA, os testes e no futuro o app das lojas dependem da API; o celular pode reenviar um comando quando o sinal volta.

## Decisão

- REST com JSON e nomes em português; uma rota por caso de uso (`POST /api/ordens/{id}/designar`).
- Erros em ProblemDetails, com o código da regra (`codigo: "RN07"`) e 422 para regra violada.
- Chave de idempotência nos comandos que o celular pode reenviar.
- Versão no caminho só quando houver quebra.
- O OpenAPI gerado é o contrato e é comparado em todo PR.

## Alternativas descartadas

- GraphQL: flexibilidade que o PWA não precisa, e cache e autorização mais difíceis.
- gRPC: o navegador não fala direto.

## Consequências

- Respostas tipadas (`TypedResults`) em todos os casos, para o OpenAPI sair completo.
- Todo comando da ordem exige o cabeçalho `Idempotency-Key` (UUID gerado pelo aparelho a cada ação). A chave fica gravada no evento da ordem: reenviar a mesma chave devolve a ordem como está, sem repetir a ação; a mesma chave em outra ordem dá 422.
- Duas ações ao mesmo tempo na mesma ordem: a versão da ordem deixa só uma passar; a outra recebe 409 e o app atualiza a tela.
