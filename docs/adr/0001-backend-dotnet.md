# ADR-001 · Back-end em .NET 10

**Status:** Aceita

## Contexto

O sistema tem muitas regras de negócio (RN01 a RN16) e uma pessoa desenvolvendo. Foram comparados .NET 10, Java com Spring Boot e Go.

## Decisão

Back-end em C# com .NET 10 (LTS).

## Alternativas descartadas

- Go descartado: modela mal um domínio com muitas regras, que se espalhariam pelo código.
- Java empatou tecnicamente; o desempate foi o código já pronto e o domínio da linguagem.

## Consequências

- LTS até novembro de 2028: a migração para o próximo LTS entra no planejamento de 2028.
- Para tirar o EXIF das fotos, usar Magick.NET ou SkiaSharp (a ImageSharp exige licença paga para empresas com receita acima de US$ 1 milhão).
