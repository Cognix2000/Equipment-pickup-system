# ADR-004 · PostgreSQL com PostGIS e o SQL revisado como primeira migração

**Status:** Aceita

## Contexto

O banco precisa de mapa (pontos dos clientes, regiões), de histórico só de acréscimo e de expurgo com prazo de guarda (LGPD).

## Decisão

- PostgreSQL 16 com PostGIS.
- A migração `ModeloInicial` aplica `Scripts/0001_modelo_inicial.sql`; mudanças novas entram como migrações novas, nunca editando esse arquivo.
- Dois usuários: `retirada_dono` aplica as migrações; `retirada_api` roda a aplicação e herda `retirada_app`, que não tem DELETE (RN12).

## Alternativas descartadas

- Gerar o esquema só pelo EF Core: gatilhos de só acréscimo, papéis e funções de expurgo não saem dele.
- SQL Server: licença paga e PostGIS bem mais maduro que o tipo geography do SQL Server para este uso.

## Consequências

- Toda tabela nova precisa de `GRANT` para `retirada_app` na própria migração.
- `tests/Retirada.Ordens.TestesBanco` confere o mapeamento contra o esquema real.
