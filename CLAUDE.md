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

Papéis: `gestor`, `financeiro`, `tecnico`, `estoque` e `sistema` (este último só para as rotinas). Gestor e financeiro usam 2FA obrigatório: a chave de acesso (passkey, digital ou rosto do aparelho) ou o código de um aplicativo autenticador (ADR-012).

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
| RF01–RF02 | Login com 2FA (aplicativo ou chave de acesso); gestão de usuários com convite, revogação e zerar o 2FA |
| RF03–RF05 | Importação da planilha com prévia e gravação |
| RF06–RF07 | Lista e detalhe da ordem |
| RF08 | Gestor designa ao técnico |
| RF09 | Financeiro cancela com motivo |
| RF11–RF13 | Técnico: rota, retirada (séries, GPS, fotos) e visita sem sucesso |
| RF14 | Conferência no estoque |
| RF15 | Devolução na loja, inteira ou parcial |
| RF16 | Histórico |
| RF18 | Parâmetros ajustáveis |
| RF19 | Baixa no HubSoft |
| Localização | O técnico manda só a última posição, com o app aberto; só o gestor vê a equipe (`/gestao/equipe`). Mapa real em produção (`Mapa:Estilo=real`, OpenFreeMap) |
| Repasse | R$ 5,00 por retirada **conferida** para quem retirou (CLT ou PJ, igual); o financeiro registra o pagamento (`Ordens:ValorPorRetiradaCentavos`) |
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

## Onde paramos (out/2026)

- **Em produção:** `https://pedro.devioslab.com.br`, ainda com dados de teste ([docs/implantacao.md](docs/implantacao.md)). **A produção só recebe tag** (`VERSAO=vX.Y.Z`; o deploy recusa `main`). Quem lança a versão é o Pedro.
- **Entregue depois do incremento 1:**

  | PR | O que entrou |
  | --- | --- |
  | #34 | Chave de acesso, a passkey (ADR-012) |
  | #35 | Resumo do técnico na tela dele |
  | #36 | Repasse ao técnico, com o registro do pagamento |
  | #39 | Mapa real, posição do técnico para o gestor e rota no Google Maps ou no Waze |

- **Próximos pedidos do Pedro, nesta ordem:**
  1. **Pagamento pelo cliente (AbacatePay).** Está **aguardando** a decisão do financeiro: o que o cliente paga, e se o HubSoft já gera o PIX.
  2. **Outras formas de login:** código por e-mail e entrar com Microsoft 365 e com Google. Faltam o remetente de e-mail e o acesso de admin do M365.
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
  - nunca commitar direto na `main`.
