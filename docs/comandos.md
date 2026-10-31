# Comandos do dia a dia

Rode os comandos na **raiz do repositório**, salvo quando o bloco começa com `cd`. Para instalar as ferramentas, veja [ambiente.md](ambiente.md).

## Tudo de uma vez

```bash
./testar.sh            # o mesmo que o CI: backend (com banco, se RETIRADA_TESTE_BANCO estiver definido), frontend e e2e
./testar.sh rapido     # sem e2e e sem banco: para conferir no meio do trabalho
./testar.sh backend    # só o backend
./testar.sh frontend   # só o frontend (tipos, lint, Vitest e e2e)
```

## Docker (homologação na sua máquina)

| Comando | O que faz |
| --- | --- |
| `docker compose up --build` | Sobe tudo e remonta as imagens (banco, migrações, dados fictícios, API, app e fotos) |
| `docker compose up -d` | Sobe em segundo plano |
| `docker compose --profile observabilidade up --build` | Sobe com o Grafana em http://localhost:3000 |
| `docker compose ps` | Mostra o que está rodando |
| `docker compose logs -f api` | Acompanha o log da API (troque por `migrador`, `web`, `banco`…) |
| `docker compose restart api` | Reinicia só a API |
| `docker compose down` | Para tudo e **mantém** os dados |
| `docker compose down -v` | Para tudo e **apaga** o banco, as fotos e as chaves. Volta ao zero, com os dados fictícios |

## Backend (.NET)

```bash
cd backend
dotnet build                                           # compila
dotnet test                                            # domínio, arquitetura e contrato (os de banco ficam ignorados)
dotnet test --filter "FullyQualifiedName~Repasse"      # só os testes com "Repasse" no nome
RETIRADA_TESTE_BANCO="Host=localhost;Database=retirada_teste;Username=retirada_dono;Password=...;GSS Encryption Mode=Disable" dotnet test
ATUALIZAR_CONTRATO=1 dotnet test tests/Retirada.Arquitetura.Testes   # mudou a API de propósito: regrava o contrato
dotnet list package --vulnerable --include-transitive  # dependência com falha conhecida
dotnet tool restore && dotnet stryker --config-file stryker-config.json   # mutação do domínio (antes da versão)
```

`RETIRADA_TESTE_BANCO` aponta para um banco **de teste** com PostGIS, nunca para o de homologação: os testes gravam nele.

### Rodar a API fora do Docker

O passo a passo está no README ("Desenvolvimento com recarga automática"). Para parar a API:

```bash
pkill -f "Retirada.Api.dl[l]"    # os colchetes evitam que o comando mate o próprio terminal
```

## Frontend (React)

```bash
cd frontend
npm ci                           # instala exatamente o que está no package-lock
npm run dev                      # http://localhost:5173, repassando /api para a API em :5080
npm run typecheck                # tipos
npm run lint                     # oxlint
npm test                         # Vitest
npm run test:e2e                 # Playwright em 390, 820 e 1280 px, com axe
npx playwright test e2e/chave-de-acesso.spec.ts      # um arquivo só
npx playwright test -g "repasse"                     # só os testes com "repasse" no nome
npm run storybook                # componentes nas três larguras
npm audit --omit=dev             # dependência com falha conhecida
ENSAIO_URL=http://localhost:8080 npm run ensaio      # roteiro de demonstração contra o sistema rodando
```

## Banco

```bash
docker compose exec banco psql -U retirada_dono -d retirada        # abre o psql no banco de homologação
```

Dentro do `psql`:

```sql
SET search_path = retirada, public;
\dt                                   -- lista as tabelas
\d ordem                              -- colunas, índices e regras de uma tabela
SELECT status, count(*) FROM ordem GROUP BY status;
SELECT tipo, ocorrido_em, ator_papel FROM evento_ordem WHERE ordem_id = '...' ORDER BY seq;
```

**Migração nova:**
1. crie `backend/src/Retirada.Banco/Scripts/NNNN_nome.sql`, com o próximo número;
2. crie a classe em `Migracoes/` com a data no nome, igual às existentes;
3. o `migrador` aplica na próxima subida.

Veja [modelo-de-dados.md](modelo-de-dados.md).

## Git

```bash
git switch main && git pull                          # começa do mais novo
git switch -c feat/42-mapa-tempo-real                # branch: tipo/nº-da-issue-descricao (CONTRIBUTING)
git status                                           # o que mudou
git add caminho/do/arquivo outro/arquivo             # SEMPRE caminhos explícitos, nunca git add -A
git commit -m "feat(ordens): mapa com a posição do técnico (#42)"
git push -u origin feat/42-mapa-tempo-real           # depois, abra o PR no GitHub
git fetch origin && git merge origin/main            # trazer a main para a sua branch (sem rebase em branch compartilhada)
git reset --soft HEAD~1                              # desfaz o último commit LOCAL, mantendo as mudanças
git restore caminho/do/arquivo                       # descarta a mudança de um arquivo
git stash && git stash pop                           # guarda o que está pela metade e traz de volta
```

**Commitou na branch errada?**
1. Rode `git log` e copie o hash do commit.
2. `git switch branch-certa && git cherry-pick <hash>` leva o commit para lá.
3. Na branch errada, `git reset --hard HEAD~1` tira o commit, desde que ainda não tenha sido enviado.

## Produção (no servidor)

Atualizar para uma versão já lançada (a produção só aceita tag, nunca `main`):

```bash
cd ~/retirada && sed -i 's/^VERSAO=.*/VERSAO=v0.2.0/' .env && sh deploy/atualizar.sh   # código e imagens na tag v0.2.0
docker compose -f docker-compose.yml -f docker-compose.producao.yml ps             # o que está rodando
docker compose -f docker-compose.yml -f docker-compose.producao.yml logs -f api    # log da API
docker compose -f docker-compose.yml -f docker-compose.producao.yml logs migrador  # resultado das migrações
curl -sI https://pedro.devioslab.com.br/health                                      # saúde de fora do servidor
```

Para lançar uma versão nova (criar a tag), veja "Lançar uma versão" em [implantacao.md](implantacao.md#lançar-uma-versão). O resto da produção (HTTPS, zerar o teste, backup e primeiro gestor) está no mesmo arquivo.
