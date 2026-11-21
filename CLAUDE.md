# CLAUDE.md · Retirada Netwise

Contexto para o Claude (VS Code, terminal ou web) e para qualquer agente de IA trabalhar neste repositório (o [AGENTS.md](AGENTS.md) aponta para cá). Leia este arquivo inteiro antes de mudar qualquer coisa.

- **Mapa da documentação:** [docs/README.md](docs/README.md).
- **Arquitetura e banco:** [docs/arquitetura.md](docs/arquitetura.md) e [docs/modelo-de-dados.md](docs/modelo-de-dados.md).
- **Branches e PRs:** [CONTRIBUTING](CONTRIBUTING.md).
- **Decisões técnicas:** [docs/adr](docs/adr/README.md).

## O que é

Sistema de **retirada de equipamentos de clientes inadimplentes** da Netwise, um provedor de internet FTTH. Ele substitui a planilha do financeiro e funciona assim:

1. O financeiro importa a planilha exportada do HubSoft.
2. A ordem passa 48 h em carência e depois fica disponível.
3. O gestor designa a ordem a um técnico.
4. O técnico recolhe no cliente, com séries, GPS e fotos.
5. O estoque confere o que chegou (ou registra a devolução na loja).
6. O financeiro dá a baixa no HubSoft.

Fluxo de status: Em carência → Disponível → Atribuída → Concluída → Conferida. Também existem Encerrada sem retirada, Cancelada e Devolvida na loja.

Papéis: `gestor`, `financeiro`, `tecnico`, `estoque` e `sistema` (este último só para as rotinas). O 2FA é opcional para todos (ADR-015, nov/2026): quem configura TOTP usa como segundo fator ao entrar; quem não configurou entra só com a senha. A **chave de acesso NÃO é segundo fator** — é um caminho alternativo de entrada (botão "Entrar com a digital ou o rosto" na tela de login), nunca obrigatório após a senha (revisão ADR-015, nov/2026). A sessão dura 30 dias com rolagem.

## Regras que não se negociam

- **Só dados fictícios** em código, teste, doc, log, issue e captura:
  - telefones com DDD `00` e e-mails `@exemplo.invalid`;
  - séries `SIMU…`/`HWTC…`;
  - "Cidade Simulada".
- **Nunca** dado real de cliente, nem nome de pessoa da empresa.
- **LGPD:** a importação descarta CPF/CNPJ, e-mail, plano e PPPoE (RN04). A foto é guardada sem EXIF (RN14). O histórico mostra só o papel de quem fez, nunca o nome.
- **Terceiros (parceiros) veem o mínimo** de dados do cliente.
- **WhatsApp:** só o oficial. O sistema registra o contato; não envia mensagem nem usa ferramenta não oficial.
- **Nada de importar `.sql`/`.bak`** de sistemas da empresa. A entrada é só a planilha `.xlsx`.
- **Tudo o que é novo fica documentado no mesmo PR:** este arquivo (requisitos e "Onde paramos"), os `docs/` que a mudança toca e, para regra de negócio, o documento do projeto, fora do repositório. A lista do que atualizar em cada caso está em [docs/README.md, "Manter atualizado"](docs/README.md#manter-atualizado). Sem documentação, não está pronto.

## Stack

- **Backend:** .NET 10 com minimal APIs, EF Core 10 (Npgsql + NetTopologySuite) e PostgreSQL 16 com PostGIS. É um **monólito modular** com os módulos `Retirada.Acesso` e `Retirada.Ordens`. O host só chama `AddModuloX`/`MapModuloX`.
- **Frontend:** React 19, Vite, TypeScript, Tailwind 4, TanStack Query, React Router e zod. Os componentes seguem o padrão shadcn em `components/ui`. É uma PWA, com a tela do técnico pensada para celular.
- **Fotos:** porta `IArmazenamentoDeFotos`, com o adaptador S3 apontando para o SeaweedFS no compose e o de pasta para dev/testes (ADR-011).
- **Testes:**
  - backend: xUnit + Shouldly, NetArchTest e o snapshot do contrato OpenAPI;
  - frontend: Vitest, e Playwright e2e em 390/820/1280 px com axe;
  - carga: k6 em `testes-carga/`.
- **Observabilidade:** OpenTelemetry, Grafana, Loki, Tempo e Prometheus (ADR-008).

## Padrões do código (siga os existentes)

- **Um caso de uso por arquivo** em `Modulos/Retirada.Ordens/Casos/`. As regras ficam no agregado `Dominio/Ordem.cs` e lançam `RegraDeNegocioException("RNxx", "mensagem para o usuário")`.
- **Todo comando da ordem passa por `ComandoDaOrdem.ExecutarAsync`.** Ele cuida de:
  - `Idempotency-Key`;
  - `versao` (concorrência otimista);
  - o evento em `evento_ordem`, na mesma transação (só acréscimo, RN12).

  Não grave a ordem por fora dele.
- **Rotinas automáticas** em `Rotinas/`: um `BackgroundService` a cada 5 min. `Lote.ProcessarAsync` trava com `FOR UPDATE SKIP LOCKED`: busca os ids por SQL e depois carrega pelo EF. Eventos de rotina usam `AutorDoEvento.Sistema`. Nos testes as rotinas ficam desligadas (`Ordens:Rotinas:Ativas=false`).
- **Mudou a API de propósito?** Rode `ATUALIZAR_CONTRATO=1 dotnet test tests/Retirada.Arquitetura.Testes` e versione `Contrato/openapi.v1.json`.
- **Frontend:**
  - os comandos ficam em `features/ordens/comandos.ts`;
  - as telas, em `paginas/{tecnico,painel,estoque}`;
  - `useComandoDaOrdem(ordemId, executar, aoConcluir)` chama `aoConcluir` no próprio hook, porque o `onSuccess` do `mutate` se perde quando o componente desmonta.
- **Textos da interface e nomes no código em português.** Use mensagens curtas que digam o que fazer.

## Armadilhas já conhecidas

- **Capturas locais do Playwright** (`e2e/**-snapshots/`) **não vão para o commit.** Use `git add` com caminhos explícitos, nunca `git add -A`.
- **axe acusando contraste:** pode ser transição de botão no meio do teste. O Playwright usa `reducedMotion: 'reduce'` e os botões usam `motion-reduce`.
- **Link `tel:` no Chromium headless** trava os cliques seguintes. O e2e cancela a navegação desses links.
- **2FA no ensaio:** cada código TOTP vale uma vez só. O ensaio espera o próximo passo de 30 s.
- **Chave de acesso (WebAuthn):** as opções e as respostas vão no JSON da própria Fido2NetLib (`ToJson`/`FromJson`), nunca no JSON da API, que troca os nomes dos enums. Teste da API com `AutenticadorDeTeste`; e2e com o autenticador virtual do Chromium (CDP `WebAuthn`). Nos testes de tela, use `{ name: 'Entrar', exact: true }`: o botão "Entrar com a digital ou o rosto" também contém "Entrar".
- **Matar a API com `pkill -f`:** use `pkill -f "Retirada.Api.dl[l]"`, senão o comando mata o próprio shell.
- **MinIO CE foi descontinuado.** Por isso o projeto usa SeaweedFS (ADR-011).

## Comandos

```bash
./testar.sh                                     # o mesmo que o CI, antes de dizer que terminou (./testar.sh rapido no meio do trabalho)
docker compose up --build                       # tudo: banco, migrações, dados fictícios, API, app, fotos → http://localhost:8080
cd backend && dotnet test                       # domínio, arquitetura e contrato (os de banco ficam ignorados)
RETIRADA_TESTE_BANCO="Host=localhost;Database=retirada_teste;Username=retirada_dono;Password=...;GSS Encryption Mode=Disable" dotnet test
cd frontend && npm run typecheck && npm run lint && npm test && npm run test:e2e
ENSAIO_URL=http://localhost:8080 npm run ensaio # roteiro de demonstração inteiro, contra a stack real
```

## Requisitos (códigos usados no código e nos commits)

O documento completo de requisitos não está no repositório. O resumo abaixo foi tirado do código:

| Código | O que é |
| --- | --- |
| RF01–RF02 | Login com 2FA **opcional** (aplicativo ou chave de acesso, ADR-015); o usuário liga/desliga TOTP na `/conta`; gestão de usuários com convite, revogação, "Apagar" (próximo passo depois de revogar: anonimiza e tira da lista, preservando RN12) e zerar o 2FA |
| RF03–RF05 | Importação da planilha com prévia e gravação; aceita qualquer planilha com cliente+endereço+cidade e preenche o resto com padrão (atualizado em nov/2026). **Tolerante ao HubSoft real** (nov/2026): linhas com tipo ≠ remoção (REATIVAÇÃO, INSTALAÇÃO, TROCA DE PLANO...) são ignoradas e a prévia destaca quantas de cada tipo; telefone enterrado em `descricao_servico` é extraído por regex; `plano` do cliente ("600 Mbps", "1 Gbps") sai como campo próprio na ordem; a observação copia só **campos rotulados seguros** (`OBSERVAÇÕES:`, `DISPONIBILIDADE PARA ATENDIMENTO:`, `CLIENTE EM CASA:`, `REMOÇÃO DE EQUIPAMENTO:`) — todo o resto da descrição é descartado (RN04 LGPD: nome do solicitante, débito, motivo, plano em texto bruto não entram). **Também cadastro manual**: o financeiro cria ordem avulsa em `/financeiro/nova-ordem` (POST `/api/ordens`, evento próprio `cadastrada_manualmente` no histórico). |
| RF06–RF07 | Lista e detalhe da ordem |
| RF08 | Gestor designa ao técnico |
| RF09 | Financeiro cancela com motivo |
| RF11–RF13 | Técnico: rota, retirada (séries, GPS, fotos) e visita sem sucesso |
| RF14 | Conferência no estoque |
| RF15 | Devolução na loja, inteira ou parcial |
| RF16 | Histórico |
| RF18 | Parâmetros ajustáveis |
| RF19 | Baixa no HubSoft |
| Localização | O técnico manda só a última posição, com o app aberto; só o gestor vê a equipe (`/gestao/equipe`). Mapa real em produção (`Mapa:Estilo=real`, OpenFreeMap). Visual "Imersivo 3D" (Pedro, nov/2026): paleta bege quente aplicada ao Liberty, prédios extrudados em ambrado com sombra lateral, pitch 55° e 3D ligado por padrão, pins de ordem em formato de gota vermelho-laranja. `aplicarEstiloImersivo()` sobrescreve o paint do estilo Liberty em runtime, sem mudar a fonte de dados |
| Repasse | R$ 5,00 por retirada **conferida** para quem retirou (CLT ou terceiro, igual; `Ordens:ValorPorRetiradaCentavos`). O CLT recebe na folha e o financeiro registra o pagamento; o terceiro resgata por PIX (linha abaixo) |
| Resgate (ADR-014) | **Terceiro** aperta "Resgatar" (`/tecnico/resgate`) e recebe por PIX pelo **Asaas**, só do que o estoque já conferiu, para a chave PIX que o financeiro aprovou; o Asaas valida com o servidor antes de pagar (`/api/webhooks/asaas/validacao`) e o aviso `TRANSFER_DONE` traz o ID de Fim a Fim e o comprovante. **CLT** recebe na folha e só acompanha; o financeiro vê a folha do mês. Campo `vinculo` no técnico. `Pagamentos:Gateway` = `asaas`, `simulado` (homologação) ou `desligado` (padrão) |
| RN01 | Reimportar não duplica |
| RN02 | Só cliente rescindido vira ordem |
| RN03 | Carência de 48 h; o financeiro pode liberar antes |
| RN04 | Descarta dado pessoal |
| RN05 | Telefone em E.164 (DDD 00 em simulação) |
| RN06 | Proteção contra fórmula no Excel |
| RN07 | Ordem cancelada bloqueia |
| RN08 | Conferência item a item |
| RN09 | 3 visitas sem sucesso, ou 10 dias sem resposta com tentativa de contato, encerram a ordem |
| RN10 | Registro de contato na janela seg–sáb, 8h–18h |
| RN11 | Série aberta em uma ordem só |
| RN12 | Histórico só de acréscimo |
| RN13 | Devolução vale após encerrar sem retirada |
| RN14 | Fotos da retirada |
| RN15 | Limite de 10 MB |
| RN16 | Nada do arquivo é executado |

RF10, RF17 e RF20 ainda não têm implementação. Peça a definição antes de começar.

## Onde paramos (nov/2026)

- **Em produção:** `https://pedro.devioslab.com.br`. O banco começou com dados de teste e, quando Pedro rodar `db/limpar-para-producao.sql` ([docs/implantacao.md, "Antes dos dados reais"](docs/implantacao.md#antes-dos-dados-reais-zerar-o-teste)), fica só o gestor `pedromendes211205@gmail.com` e os `parametro`. **A produção só recebe tag** (`VERSAO=vX.Y.Z`; o deploy recusa `main`). Quem lança a versão é o Pedro. O workflow **"Implantar na produção"** ([.github/workflows/implantar.yml](.github/workflows/implantar.yml)) atualiza o servidor por um **self-hosted runner** instalado no próprio servidor (rótulo `producao`), porque o SSH público é fechado pelo ufw — só a VPN WireGuard entra. Aprovação do Pedro pelo Environment `producao` no GitHub.
- **Entregue depois do incremento 1:**

  | PR | O que entrou |
  | --- | --- |
  | #34 | Chave de acesso, a passkey (ADR-012) |
  | #35 | Resumo do técnico na tela dele |
  | #36 | Repasse ao técnico, com o registro do pagamento |
  | #39 | Mapa real, posição do técnico para o gestor e rota no Google Maps ou no Waze |
  | #43 | Valor do equipamento da planilha com ponto decimal (não vira mais milhar) |
  | #47 | Node 26 no CI e no guia do ambiente, igual ao Docker |
  | #48 | Backup diário cifrado (age), com cópia fora do servidor (rclone) e restauração (ADR-013) |
  | #50 | Resgate do técnico terceiro por PIX pelo Asaas, vínculo CLT/terceiro e folha do mês (ADR-014) |
  | #52 | Importação tolerante: aceita planilha com só cliente+endereço+cidade (RF04 revisado) |
  | #53 | `System.Formats.Cbor` 11.0.0 para cobrir o advisory GHSA-vmv3-wvjp-wqpj (transitivo do Fido2) |
  | #54 | Carência ajustável por ordem: botão "Ajustar carência" no detalhe da ordem (RN03 revisada) |
  | #55 | Cadastro manual de ordem pelo financeiro em `/financeiro/nova-ordem` (POST `/api/ordens`, RF03 estendido) |
  | #56 | Ordenação da lista do técnico por proximidade (GPS), por urgência e por bairro |
  | #57 | Mapa 3D no técnico: prédios extrudados via OpenMapTiles sobre o mapa real, botão 2D/3D no canto, preferência salva por sessão |
  | #59 | Deploy automático em produção: workflow "Implantar na produção" (dispara em `release.published`, Environment `producao` pede aprovação do Pedro). Hoje roda no self-hosted runner do servidor, não por SSH (veja acima) |
  | #73 | Importação: campo `plano` como coluna própria da ordem; `OBSERVACAO` sai do texto livre por **allow-list rígida** (OBSERVAÇÕES, DISPONIBILIDADE, CLIENTE EM CASA, REMOÇÃO DE EQUIPAMENTO, LOCALIZAÇÃO ZONA RURAL) — descarta SOLICITANTE, PLANO em texto bruto, DÉBITO, MOTIVO, TELEFONE (RN04); prévia agrupa e exibe linhas ignoradas por tipo (REATIVAÇÃO, INSTALAÇÃO, TROCA DE PLANO...) |
  | #74 | Botão Sair com confirmação (modal `<dialog>` nativo, acessível, sem lib): evita deslogar sem querer no celular. Componente `BotaoDeSair` em `features/acesso/`, usado no `LayoutPainel` e no `LayoutTecnico` |
  | #75 | Mapa Imersivo 3D (Pedro, nov/2026, Opção 2 do comparativo): paleta bege quente sobre o Liberty via `aplicarEstiloImersivo()` (sobrescreve paint em runtime, sem trocar a fonte), prédios extrudados com interpolação base/meio/topo e luz lateral ambrada, pins em formato de gota vermelho-laranja; 3D ligado por padrão (só `localStorage === 'false'` força 2D) |
  | #77 | Deploy aceita a tag com ponto extra (`v.0.3.0`, criada por engano): `implantar.yml` e `deploy/atualizar.sh` validam `^v\.?X.Y.Z$`; se a `VERSAO=vX.Y.Z` do `.env` não existir como tag, o script tenta `v.X.Y.Z` e grava no `.env` a tag que achou |
  | — | Guarda da `main` sem plano pago (ADR-006): o GitHub Free não protege branch em repositório privado (403). O hook `.githooks/pre-push` bloqueia o push local (`git config core.hooksPath .githooks`) e o workflow `guarda-da-main.yml` abre issue para commit fora de PR. O ruleset fica pronto em `.github/protecao-main.json` para quando houver o Team. Os dois commits da linha abaixo motivaram a mudança |
  | — | Direto na `main`, sem PR: `implantar.yml` ganhou `packages: read` para puxar as imagens do GHCR (`61bb5d5`); `OrdemDto` passou a expor `Plano`, com o contrato `openapi.v1.json` atualizado (`d6c4b6b`, complemento do #73) |
  | — | Login do GHCR no deploy automático isolado em `DOCKER_CONFIG=$RUNNER_TEMP/docker`: o `docker login` com o `github.token` (que vence no fim do job) sobrescrevia o `~/.docker/config.json` do `cladmin` e o `sh deploy/atualizar.sh` manual dava `denied`. O deploy manual usa o token clássico `read:packages` |
  | — | Disco do servidor (84% de uso): `implantar.yml` mantém só os 3 `.env.bak.*` mais recentes a cada deploy; `docker-compose.producao.yml` reduz `max-file` de 5 para 3 nos logs `json-file` de todo serviço |

- **Versão:** a última é a `v0.3.2` (no `d6c4b6b`). Existe também a tag `v.0.3.0`, com ponto extra, criada por engano; não crie outras assim, use sempre `vX.Y.Z`. O agente não tem permissão para criar release: quem cria é o Pedro.
- **Pagamento pelo cliente:** fica **manual** (decisão de nov/2026, Pedro). O cliente paga por fora do sistema — nada de integrar PIX do cliente pelo app. Mantido o resgate ao técnico por PIX (ADR-014), que é outro fluxo.
- **Próximos pedidos do Pedro, nesta ordem:**
  1. **Notificações push no PWA** (ainda a fazer): alinhadas com o SO do técnico; usar Web Push padrão (VAPID), sem depender de serviço proprietário. Serve para avisar ordem designada, prazo de carência vencendo e lembrete de visita.
  2. **Microsoft 365 e Google** no login (ainda a fazer): dependem do admin do M365 e do console do Google Cloud. A senha + TOTP (opcional) + chave de acesso passwordless já atendem o desktop sem digital.
- **Limpeza do disco:** o `deploy/limpar-disco.sh` roda num timer do systemd todo dia às 03:30, apagando imagens Docker não usadas, cache de build, backups antigos do `.env`, cache do apt e logs do journal com mais de 7 dias ([docs/implantacao.md](docs/implantacao.md#limpeza-diária-do-disco)).
- **Backup na produção:** o script está pronto, mas falta o Pedro gerar a chave do age, preencher `BACKUP_*` no `.env`, configurar o rclone (ou o Veeam) e agendar no cron ([docs/implantacao.md](docs/implantacao.md#backup-automático)).
- **Resgate na produção:** o código está pronto e desligado. Falta a Netwise abrir a conta no Asaas (CNPJ, só para os resgates), pôr a chave e o token no `.env` e configurar os dois webhooks ([docs/implantacao.md](docs/implantacao.md#resgate-por-pix-asaas)). Conferir no ambiente de testes do Asaas o formato da chave de celular.
- **Pendências opcionais, para antes da tag `v1.0.0`:** versionar as capturas de referência das telas e incluir a telemetria do navegador (Faro, ADR-008).
- **Não comece funcionalidade nova sem o Pedro pedir.**

## Como colaborar com o Pedro

- **Postura:**
  - responda em português do Brasil, direto, explicando o raciocínio nas decisões técnicas;
  - atue como dev sênior, com clareza, segurança e manutenção em primeiro lugar;
  - na hora de um erro, vá atrás da causa raiz.
- **Antes de mudar:**
  - leia o contexto antes de sugerir;
  - prefira a solução simples e consistente com o que já existe;
  - avise o impacto antes de uma mudança grande.
- **Ao sugerir código:** diga em qual arquivo ele entra.
- **Ao revisar:** aponte primeiro os bugs, os riscos e as melhorias práticas, e só depois os elogios.
- **Fluxo de trabalho:**
  - branch curta a partir da `main`, com Conventional Commits em português;
  - PR com o CI verde e squash merge;
  - nunca commitar direto na `main`;
  - **documentar tudo o que é novo no mesmo PR** (veja "Regras que não se negociam").
