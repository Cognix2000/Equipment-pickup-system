# ADR-003 · Modelo híbrido dentro de cada módulo

**Status:** Aceita

## Contexto

As regras de negócio precisam ficar num lugar só, testáveis sem banco, e várias integrações vão ser trocadas (planilha e API do HubSoft, GeoJSON e API do mapa, wa.me e API oficial do WhatsApp, fotos no S3).

## Decisão

Cada módulo tem estas pastas:

| Pasta | O que guarda | Regra |
| --- | --- | --- |
| `Dominio/` | Agregados, objetos de valor e regras | Não conhece EF Core, ASP.NET nem banco |
| `Casos/` | Um arquivo por caso de uso: endpoint, validação e execução | Comando passa pelo agregado; consulta lê direto do banco (CQRS leve) |
| `Portas/` | Interfaces do que o módulo precisa de fora | O domínio e os casos só falam com a porta |
| `Infraestrutura/` | EF Core e adaptadores | Trocar o adaptador não muda o resto |
| `Contratos/` | O que outros módulos podem usar | Módulo nunca acessa o interior de outro |

`Portas/` e `Contratos/` nascem quando o primeiro caso precisar deles.

## Alternativas descartadas

- Clean Architecture completa: quatro projetos por módulo, cerca de 24 no total, com cerimônia demais para uma pessoa.
- Vertical Slice pura: as regras se repetiriam entre as fatias.
- Camadas clássicas (controller, service, repository): as regras vazariam para os services.

## Consequências

- `tests/Retirada.Arquitetura.Testes` quebra o build se o domínio depender de EF Core ou ASP.NET, se um caso de uso ficar público ou se um módulo usar o interior de outro.
- Módulo novo precisa entrar na lista `Modulos` desse teste.
