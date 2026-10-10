# ADR-002 · Monólito modular

**Status:** Aceita

## Contexto

Poucas dezenas de usuários, um banco, uma pessoa desenvolvendo, e módulos que crescem a cada incremento (parceiros, custódia, remuneração).

## Decisão

Um deploy e um banco, com módulos isolados: cada módulo tem domínio, banco e endpoints próprios, e o host só chama `AddModuloX` e `MapModuloX`.

## Alternativas descartadas

- Monólito simples: vira um emaranhado quando entrarem parceiros e remuneração.
- Microsserviços: rede, consistência distribuída e vários deploys sem ganho nessa escala.

## Consequências

- Um módulo pode virar serviço no futuro sem reescrever o domínio.
- Módulos conversam por contratos e eventos de domínio no mesmo processo.
