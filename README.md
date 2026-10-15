# Retirada Netwise

Sistema de retirada de equipamentos de clientes rescindidos, substituindo a planilha do financeiro.
Este repositório é o esqueleto do incremento 1, com:

- API .NET 10;
- PWA em React;
- PostgreSQL com PostGIS no Docker.

Tudo roda com dados fictícios: nenhum dado real de cliente entra aqui.

## Como rodar

### Tudo no Docker (homologação)

```bash
cp .env.example .env          # em homologação, troque as senhas
docker compose up --build
```

Depois de subir, os endereços são estes:

| O quê | Endereço |
| --- | --- |
| Login | http://localhost:8080/entrar |
| App do técnico | http://localhost:8080/tecnico/ordens |
| Painel do financeiro | http://localhost:8080/financeiro/ordens |
| Gestão de usuários | http://localhost:8080/gestao/usuarios |
| Saúde da API | http://localhost:8080/health |

O serviço `migrador` aplica as migrações e a carga fictícia (`db/dados-ficticios/demo.sql`) e depois sai. A API só sobe se ele terminar bem.

### Entrar na homologação

Os usuários fictícios (`@exemplo.invalid`) recebem a senha `Demo@Retirada2026` quando o migrador roda com `--dados-ficticios`. Isso só vale para esses e-mails; ninguém real recebe senha padrão.

| Usuário | Papel | 2FA |
| --- | --- | --- |
| `tecnico.demo@exemplo.invalid` | Técnico | não |
| `financeiro.demo@exemplo.invalid` | Financeiro | sim (configura no 1º login) |
| `gestor.demo@exemplo.invalid` | Gestor | sim (configura no 1º login) |

Para o 2FA, use qualquer aplicativo autenticador (Google Authenticator, Microsoft Authenticator, etc.).

### Primeiro gestor em produção

Em produção não há carga fictícia. O primeiro gestor entra por convite, gerado na linha de comando:

```bash
docker compose run --rm migrador convidar-gestor --nome "Nome do Gestor" --email gestor@empresa --url https://retirada.exemplo
```

O comando mostra o link (vale 48 h, uma vez só). A partir daí, o gestor cadastra os outros usuários pela tela de gestão.

### Importar a planilha

Em **Importar**, o financeiro envia o arquivo e vê a prévia linha a linha antes de gravar: ordens novas, atualizações, pendências, linhas com erro e colunas descartadas. Nada entra sem a confirmação.

- **Formatos:** `.xlsx`, `.xls`, `.ods`, `.csv`, `.txt`, `.json` e `.xml`, até 10 MB. O formato é reconhecido pelo conteúdo; `.sql` e PDF são recusados.
- **Colunas:** reconhecidas pelo nome, tanto as do HubSoft (`nome_razaosocial`, `tipo_ordem_servico`...) quanto as da planilha do financeiro ("Cliente", "Telefone 1"...). CPF/CNPJ, e-mail, plano, PPPoE e afins são descartados e nunca gravados (RN04).
- **Reenviar o mesmo arquivo não duplica:** a chave é o id da OS, o protocolo ou, sem os dois, uma chave provisória. Uma planilha nova só atualiza o que mudou.
- **Para testar em homologação:** `ferramentas/dados-ficticios/planilha_retiradas_ficticia.xlsx` (formato do financeiro) e `modelo_exportacao_financeiro.xlsx` (formato do HubSoft, uma linha por equipamento).

A prévia guarda só o resumo e o hash do arquivo. Na confirmação, o PWA manda o mesmo arquivo de novo, e o servidor confere o hash antes de gravar. Assim, nenhum dado pessoal fica parado entre a prévia e a confirmação.

### Contato com o cliente

Na ordem, o técnico toca em **Ligar** e, ao voltar, marca como foi: atendeu, não atendeu, caixa postal ou número não existe (RN10). Cada registro vira uma tentativa de contato e uma linha no histórico. O resultado também marca o telefone: um número dado como inexistente deixa de oferecer a ligação.

- O sistema não liga nem manda mensagem: quem contata é a pessoa, pelo celular ou pelo WhatsApp oficial. A API já aceita registrar o WhatsApp (`canal: whatsapp`), que o financeiro vai usar quando o painel tiver o detalhe da ordem.
- O app só oferece ligar de segunda a sábado, das 8h às 18h (horário de Brasília). Um registro feito fora desse horário é aceito, mas fica marcado (`fora_da_janela`) para o gestor ver.
- O histórico guarda só o id do telefone, nunca o número.

### Rotinas automáticas

A API roda sozinha, a cada 5 minutos, o que não depende de ninguém clicar:

- **Fim da carência (RN03):** a ordem Nova cujas 48 h venceram fica Disponível. O histórico mostra "Fim da carência, ficou disponível automaticamente".

Com mais de uma instância da API, cada ordem é liberada uma vez só: a rotina trava as linhas que pega e pula as que outra ação está mudando. Para ajustar, use `Ordens__Rotinas__IntervaloMinutos` (padrão 5) ou desligue com `Ordens__Rotinas__Ativas=false`.

### Chaves da sessão (fazer backup)

O volume `chaves` guarda as chaves do ASP.NET Data Protection, que assinam o cookie de sessão e cifram os segredos do 2FA. Se ele se perder, todos saem da sessão e **gestor e financeiro precisam configurar o 2FA de novo** (um gestor zera o 2FA na tela de usuários). Faça backup dele junto com o banco.

### Desenvolvimento com recarga automática

```bash
cp .env.example .env
docker compose up -d banco migrador                      # banco + migrações + dados fictícios

cd backend/src/Retirada.Api
ASPNETCORE_ENVIRONMENT=Development dotnet run --urls http://localhost:5080

cd frontend
npm install
npm run dev                                              # http://localhost:5173 (repassa /api para :5080)
```

## Estrutura

```
backend/
  src/Retirada.Api/           host: monta os módulos; "migrar" aplica as migrações
  src/Retirada.Comum/         base do domínio (Entidade, RegraDeNegocioException)
  src/Retirada.Banco/         migrações; a primeira aplica o modelo revisado (Scripts/0001_modelo_inicial.sql)
  src/Modulos/Retirada.Ordens/
    Dominio/                  agregado Ordem, Equipamento, objetos de valor
    Casos/                    um arquivo por caso de uso: consultas (ListarOrdens, ObterOrdem) e comandos
                              (Designar, RegistrarRetirada, Conferir...), que passam todos por ComandoDaOrdem
    Infraestrutura/           EF Core mapeando o esquema "retirada"
    ModuloOrdens.cs           porta de entrada do módulo
  tests/                      unidade por módulo; Retirada.Api.Testes sobe a API inteira contra o banco;
                              arquitetura e contrato da API (Contrato/openapi.v1.json)
frontend/
  src/components/ui/          componentes no padrão shadcn/ui (Radix + Tailwind)
  src/features/               ordens, mapa, importação
  src/paginas/                técnico (celular) e painel do financeiro (web)
  e2e/                        Playwright: acessibilidade, rolagem lateral e capturas
db/
  init/                       cria o usuário de login da API (sem DELETE)
  dados-ficticios/demo.sql    as ordens dos protótipos
  testes/                     testes de comportamento do SQL (rodar à mão)
ferramentas/dados-ficticios/  gerador da planilha fictícia, mock do HubSoft e cidade simulada
testes-carga/                 k6: carga, estresse e fumaça contra homologação
docs/adr/                     decisões técnicas, com contexto e consequências
.github/                      CI, segurança, modelos de issue e PR, rótulos do kanban
```

Como trabalhar no repositório (branches, commits, PR e definição de pronto): [CONTRIBUTING.md](CONTRIBUTING.md).

## Decisões técnicas

Todas estão em [docs/adr](docs/adr/README.md). Resumo:

- **Monólito modular:** cada módulo tem domínio, banco e endpoints próprios, e o host só chama `AddModuloX` e `MapModuloX`. Ordens é o primeiro módulo; importação, estoque e usuários entram como módulos irmãos.
- **O SQL revisado é a primeira migração:** gatilhos de só acréscimo, papéis e expurgo não saem do EF Core. A migração `ModeloInicial` aplica o arquivo, e mudanças novas entram como migrações novas.
- **Dois usuários no banco:**
  - `retirada_dono` aplica as migrações;
  - `retirada_api` roda a aplicação e herda `retirada_app`, que não tem DELETE (RN12).
- **Concorrência otimista:** a coluna `versao` sobe a cada mudança. De dois aceites ao mesmo tempo, só um grava; o teste `Dois_aceites_ao_mesmo_tempo_so_um_vence` prova isso contra o banco.
- **Regras no domínio:** cada método da `Ordem` aplica a regra e lança `RegraDeNegocioException` com o código do documento (RN07, RN08…). A API devolve 422 com esse código.
- **PWA:** fontes empacotadas e mapa sem servidor externo, então funciona offline. A API nunca vem do cache do service worker, para uma ordem cancelada aparecer na hora (RN07).
- **Mapa:** MapLibre desenhando a cidade simulada a partir do GeoJSON. A API de mapa real entra no incremento 4 trocando só o estilo.

### Bibliotecas de interface

| Necessidade | Biblioteca |
| --- | --- |
| Base | React 19, TypeScript, Vite |
| Estilo | Tailwind CSS 4, com os tokens da Netwise em `src/index.css` |
| Componentes | padrão shadcn/ui (Radix UI + class-variance-authority) |
| Dados e tabelas | TanStack Query, TanStack Table |
| Formulários | React Hook Form + Zod |
| Mapa | MapLibre GL |
| Ícones e animação | Lucide, Motion |
| PWA | vite-plugin-pwa |
| Qualidade visual | Storybook, Playwright, axe |

## Testes

| Tipo | Comando | No CI |
| --- | --- | --- |
| Unidade, arquitetura e contrato da API | `cd backend && dotnet test` | Todo PR |
| Integração com o banco e a API inteira (login, limites, comandos da ordem) | `RETIRADA_TESTE_BANCO=... dotnet test` | Todo PR |
| Telas: ponta a ponta, visual e acessibilidade | `cd frontend && npm run test:e2e` | Todo PR |
| Mutação do domínio (mínimo 80%) | `cd backend && dotnet tool restore && dotnet stryker --config-file stryker-config.json` | Antes da versão |
| Carga e estresse | `k6 run testes-carga/ordens.js` (`CENARIO=estresse`) | Antes da versão |
| Segurança | CodeQL e Dependabot; OWASP ZAP | Todo PR; antes da versão |

```bash
cd backend && dotnet test                           # domínio, arquitetura e contrato; os de banco ficam ignorados
RETIRADA_TESTE_BANCO="Host=localhost;Database=retirada_teste;Username=retirada_dono;Password=...;GSS Encryption Mode=Disable" dotnet test

cd frontend
npm test                                            # Vitest: validação de arquivo e prazos
npm run test:e2e                                    # Playwright em 390, 820 e 1280 px
npm run storybook                                   # componentes nas três larguras
```

`RETIRADA_TESTE_BANCO` tem de apontar para um banco **de teste** com PostGIS. Os testes de banco criam registros nele.

**Capturas de referência:** na primeira execução, o Playwright cria as capturas que faltam e passa. Gere a versão oficial com o mesmo navegador do CI: o job "PWA React" anexa as capturas novas no artefato `playwright`. Versione essas capturas em `frontend/e2e/telas.spec.ts-snapshots/`. Depois disso, toda mudança visual precisa de aprovação.

## Observabilidade

A API exporta logs, métricas e rastros por OpenTelemetry quando `OTEL_EXPORTER_OTLP_ENDPOINT` está definido ([ADR-008](docs/adr/0008-observabilidade.md)). Para ver tudo no Grafana, na sua máquina:

```bash
# no .env: OTEL_ENDPOINT=http://observabilidade:4317
docker compose --profile observabilidade up --build
# Grafana em http://localhost:3000 (admin/admin), com Loki, Tempo e Prometheus já configurados
```

## O que falta no incremento 1

- Fotos da retirada (RF12, RN14): o comando já grava séries e GPS; falta o armazenamento das evidências.
- Encerrar a ordem após 10 dias sem resposta (`limite_dias_sem_resposta`): as tentativas de contato já são registradas; falta a rotina que conta os dias.
- Detalhe da ordem no painel do financeiro, com liberar, cancelar e registrar o WhatsApp.
- Devolução na loja só de parte dos itens (RF15); hoje encerra a ordem inteira.
