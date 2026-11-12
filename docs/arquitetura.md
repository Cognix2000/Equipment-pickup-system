# Arquitetura

Como as partes do sistema se ligam, o caminho de um pedido e o ciclo de vida da ordem. O **porquê** de cada escolha está nas [ADRs](adr/README.md); aqui está o **como**.

## Visão geral

Um servidor só, com tudo em contêineres. Da internet, só o Caddy é alcançável (portas 80 e 443). O banco e as fotos ficam na rede interna do Docker.

```mermaid
flowchart LR
    subgraph Usuarios["Quem usa"]
        Tecnico["Técnico<br/>(celular)"]
        Painel["Financeiro, gestor e estoque<br/>(navegador)"]
    end

    subgraph Servidor["Servidor (Docker)"]
        Caddy["proxy · Caddy<br/>HTTPS automático"]
        Web["web · nginx<br/>PWA React (arquivos estáticos)"]
        Api["api · .NET 10<br/>módulos Acesso e Ordens"]
        Banco[("banco · PostgreSQL 16<br/>+ PostGIS")]
        Fotos[("fotos · SeaweedFS<br/>compatível com S3")]
        Migrador["migrador<br/>aplica as migrações e sai"]
    end

    Obs["observabilidade · Grafana<br/>(OpenTelemetry, opcional)"]

    Tecnico -->|HTTPS| Caddy
    Painel -->|HTTPS| Caddy
    Caddy -->|"/api, /health"| Api
    Caddy -->|resto| Web
    Api --> Banco
    Api --> Fotos
    Migrador --> Banco
    Api -.->|logs, métricas, rastros| Obs
```

- **Homologação local:** não tem o Caddy. O `web` (nginx) atende em `:8080` e repassa `/api` para a API.
- **Produção:** usa as imagens prontas do GHCR, publicadas pelo workflow "Publicar imagens" ([implantacao.md](implantacao.md)).

## Backend: monólito modular

Um processo só, com módulos separados (ADR-002 e ADR-003). O host não conhece as regras: só chama `AddModuloX` e `MapModuloX`.

Os testes de arquitetura (NetArchTest, `tests/Retirada.Arquitetura.Testes`) garantem três coisas:
- o domínio não conhece banco nem web;
- os casos de uso não saem do módulo;
- um módulo só usa o contrato público do outro.

```mermaid
flowchart TB
    Host["Retirada.Api<br/>host: Program.cs, CSRF, sessão, limite de requisições, erros"]
    Acesso["Retirada.Acesso<br/>login, 2FA, chave de acesso, usuários, convites"]
    Ordens["Retirada.Ordens<br/>ordens, importação, contato, fotos, conferência, repasse, resgate por PIX, localização"]
    Comum["Retirada.Comum<br/>Entidade, RegraDeNegocioException, papéis e políticas"]
    BancoProj["Retirada.Banco<br/>migrações (Scripts/NNNN_*.sql)"]

    Host --> Acesso
    Host --> Ordens
    Host --> BancoProj
    Acesso --> Comum
    Ordens --> Comum
```

### Dentro de um módulo

| Pasta | O que tem | Exemplo |
| --- | --- | --- |
| `Dominio/` | Regras puras, sem banco nem HTTP. Uma regra violada lança `RegraDeNegocioException("RNxx", "mensagem")` | `Ordem.cs`, `PagamentoDeRepasse.cs` |
| `Casos/` | **Um caso de uso por arquivo**: o endpoint, o que ele lê e o que ele grava | `RegistrarRetirada.cs`, `MeuRepasse.cs` |
| `Infraestrutura/` | EF Core e SQL: mapeia as tabelas do esquema `retirada` | `OrdensDbContext.cs` |
| `Portas/` | Interfaces para o mundo de fora, com adaptadores trocáveis | `IArmazenamentoDeFotos` (S3 ou pasta) |
| `Rotinas/` | O que roda sozinho a cada 5 min | fim da carência (RN03), dias sem resposta (RN09) |
| `Importacao/` | Leitura da planilha, limpeza e prévia | — |
| `Seguranca/` (Acesso) | Senha, 2FA e WebAuthn | `HashDeSenha.cs`, `CerimoniaDaChave.cs` |

## O caminho de um pedido

Todo pedido passa pelo host antes de chegar ao caso de uso. A ordem dos filtros importa e está em `Program.cs`.

```mermaid
flowchart LR
    Pedido["Pedido do PWA"] --> Csrf{"Comando sem o<br/>cabeçalho X-Retirada: 1?"}
    Csrf -->|sim| R400["400"]
    Csrf -->|não| Sessao["Sessão: cookie HttpOnly<br/>confere usuário e carimbo no banco"]
    Sessao --> Limite{"Passou do limite<br/>por usuário ou IP?"}
    Limite -->|sim| R429["429"]
    Limite -->|não| Papel{"Papel e etapa<br/>do login permitem?"}
    Papel -->|não| R401["401 ou 403"]
    Papel -->|sim| Caso["Caso de uso"]
    Caso -->|regra violada| R422["422 com o código da regra"]
    Caso -->|ok| R200["200"]
```

### Um comando da ordem

Toda ação que muda a ordem passa por `ComandoDaOrdem.ExecutarAsync` (ADR-009): liberar, designar, retirar, conferir, cancelar e as outras. Não grave a ordem por fora dele.

```mermaid
sequenceDiagram
    autonumber
    participant App as PWA
    participant Caso as Casos/Conferir.cs
    participant Cmd as ComandoDaOrdem
    participant Ordem as Dominio/Ordem.cs
    participant BD as PostgreSQL

    App->>Caso: POST /api/ordens/{id}/conferencia + Idempotency-Key
    Caso->>Cmd: ExecutarAsync(ação, gravarJunto)
    Cmd->>BD: A chave já foi usada?
    alt reenvio (o celular mandou de novo)
        Cmd-->>App: 200 com a ordem como está, sem repetir
    end
    Cmd->>BD: carrega a ordem (técnico só a dele: senão 404)
    Cmd->>Ordem: aplica a regra
    Ordem-->>Cmd: gera um evento ou lança RegraDeNegocioException (422)
    rect rgba(0, 89, 189, 0.08)
        Note over Cmd,BD: uma transação só
        Cmd->>BD: grava a ordem (versao barra duas ações ao mesmo tempo: 409)
        Cmd->>BD: grava o evento em evento_ordem (só acréscimo, RN12)
        Cmd->>BD: gravarJunto (ex.: crédito do repasse na conferência)
    end
    Cmd-->>App: 200 com a ordem atualizada
```

## Ciclo de vida da ordem

```mermaid
stateDiagram-v2
    [*] --> nova: importada da planilha
    nova --> disponivel: fim da carência de 48 h (rotina)<br/>ou o financeiro libera antes (RN03)
    disponivel --> atribuida: gestor designa (RF08)
    atribuida --> concluida: técnico retira, com séries, GPS e fotos (RF12)
    atribuida --> disponivel: visita sem sucesso (RF13)
    atribuida --> encerrada_sem_retirada: 3ª visita sem sucesso (RN09)
    disponivel --> encerrada_sem_retirada: 10 dias sem resposta,<br/>com contato tentado (RN09)
    concluida --> conferida: estoque confere item a item (RN08)<br/>e o técnico ganha o repasse
    nova --> cancelada: financeiro cancela (RF09)
    disponivel --> cancelada
    atribuida --> cancelada
    disponivel --> devolvida_na_loja: cliente devolveu tudo na loja (RF15)
    encerrada_sem_retirada --> devolvida_na_loja: devolução depois de encerrar (RN13)
    conferida --> [*]
    cancelada --> [*]
    devolvida_na_loja --> [*]
```

- **Divergência na conferência** (série diferente ou item faltando): a ordem continua `concluida`, e os itens ficam marcados.
- **Devolução na loja:** vale em qualquer status aberto, enquanto houver item com o cliente. Com tudo devolvido, a ordem vira `devolvida_na_loja`; com parte, segue com o que falta recolher.
- **Baixa no HubSoft** (RF19): é uma marca à parte, não um status.

## Frontend

```mermaid
flowchart LR
    Paginas["paginas/<br/>uma tela por arquivo<br/>(tecnico, painel, estoque, acesso, conta)"]
    Features["features/<br/>consultas, comandos e schemas zod<br/>(ordens, acesso, repasses, mapa, importacao)"]
    UI["components/ui/<br/>botão, cartão, selo (padrão shadcn)"]
    Api["lib/api.ts<br/>fetch + cabeçalho anti-CSRF<br/>+ validação da resposta"]

    Paginas --> Features
    Paginas --> UI
    Features --> Api
    Api -->|"/api/..."| Backend["API"]
```

- **Dados do servidor:** TanStack Query. O cache invalida pela chave: `['ordens']`, `['repasses']`, `['acesso', …]`.
- **Toda resposta passa por um schema zod.** Se a API mudar o contrato, a tela falha com uma mensagem clara, e não mostra dado quebrado.
- **Guardas de rota** (`RotaProtegida`): só evitam abrir uma tela proibida. Quem barra de verdade é o servidor (RNF04).
- **Mapa:** MapLibre, carregado depois da lista (`lazy`). `GET /api/mapa` diz o estilo:
  - `real` em produção (`Mapa__Estilo` no compose): ruas do OpenFreeMap, grátis, sem chave e com uso comercial permitido;
  - `simulado` na homologação: a cidade fictícia em `public/dados-ficticios/`, porque as coordenadas dos dados de teste não caem em ruas de verdade.

  Sem geocodificação: a planilha do HubSoft não traz coordenada, e o Nominatim público proíbe dado pessoal e uso recorrente. A rota abre o Google Maps ou o Waze pelo endereço escrito, e a ordem ganha o pino no primeiro GPS registrado (retirada ou visita sem sucesso).

  **Modo 3D:** no mapa real (OpenFreeMap / OpenMapTiles), o técnico pode trocar para 3D pelo botão no canto do mapa. Um layer de `fill-extrusion` sobre a source `openmaptiles` eleva os prédios pela `render_height` do OpenMapTiles (ou 3 m × andares, quando só o número de andares vem). A preferência fica em `localStorage` (chave `mapa:modo3d`). A cidade simulada não tem prédios, então o botão não aparece.
- **Localização do técnico:** `useCompartilharPosicao` (em `LayoutTecnico`) usa `watchPosition` só com o app visível e manda para `POST /api/localizacao/minha` a cada 30 s, ou antes se andou 75 m. O gestor vê em `/gestao/equipe` (`GET /api/localizacao/equipe`, atualiza a cada 15 s). Estoque e financeiro não têm acesso.

## Resgate por PIX (ADR-014)

O técnico terceiro resgata o saldo das retiradas conferidas. O dinheiro sai pelo Asaas, atrás da porta `IGatewayDePagamento`; na homologação, o adaptador é o simulado. Quem diz que o PIX caiu é o aviso do gateway, e não a resposta do pedido:

```mermaid
sequenceDiagram
    participant T as App do técnico
    participant A as API
    participant BD as PostgreSQL
    participant G as Asaas
    T->>A: POST /api/resgates
    A->>BD: grava o resgate "solicitado" e reserva os créditos (uma transação)
    A->>G: POST /v3/transfers (externalReference = id do resgate)
    G-->>A: transferência PENDENTE → resgate "enviado"
    G->>A: POST /api/webhooks/asaas/validacao
    A-->>G: APPROVED só se bate com um resgate em andamento
    G->>A: POST /api/webhooks/asaas/eventos (TRANSFER_DONE + ID de Fim a Fim)
    A->>BD: resgate "pago" vira repasse_pagamento (ou "falhou" e o crédito volta)
    T->>A: GET /api/resgates/meu (a cada 5 s enquanto está a caminho)
```

Os webhooks não têm sessão nem o cabeçalho anti-CSRF: o token `asaas-access-token` é que autentica. A rotina de 5 min concilia o resgate que ficou parado.

## Onde mexer para…

| Quero… | Onde |
| --- | --- |
| Uma ação nova na ordem | O método em `Dominio/Ordem.cs`, um arquivo em `Casos/` usando `ComandoDaOrdem`, o mapeamento em `ModuloOrdens.cs`, os testes de domínio e de API, e o contrato |
| Uma consulta nova | Um arquivo em `Casos/` (SQL ou EF, sem passar pelo agregado), o mapeamento e o contrato |
| Uma tabela nova | Uma migração SQL nova ([comandos.md](comandos.md#banco)) e o [modelo-de-dados.md](modelo-de-dados.md) |
| Um gateway de pagamento novo | Um adaptador de `Portas/IGatewayDePagamento.cs` em `Infraestrutura/Pagamentos/` e a escolha em `ModuloOrdens.cs` (`Pagamentos:Gateway`). Os avisos dele entram como `Casos/WebhooksDoAsaas.cs` |
| Um parâmetro ajustável | `ParametrosDasOrdens` em `ComandoDaOrdem.cs` e a leitura da configuração em `ModuloOrdens.cs` (ex.: `Ordens:ValorPorRetiradaCentavos`) |
| Uma tela nova | A tela em `paginas/`, a consulta em `features/`, a rota em `rotas.tsx` e o teste em `e2e/telas.spec.ts` (acessibilidade e larguras) |
