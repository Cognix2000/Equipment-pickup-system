# Roteiro de demonstração · Incremento 1

O fluxo inteiro, do jeito que acontece na Netwise: a planilha chega, a ordem é liberada, o técnico recolhe, o estoque confere e o financeiro dá a baixa. Tudo com dados fictícios (DDD 00, e-mails `@exemplo.invalid`, séries `SIMU`/`HWTC`).

Duração: uns 15 minutos. Este roteiro foi ensaiado de ponta a ponta contra a API, o banco e o armazenamento de fotos de verdade (`npm run ensaio`, no fim).

## 1. Preparar (na véspera)

```bash
cp .env.example .env            # troque as senhas se a máquina for compartilhada
docker compose up --build       # primeira vez: alguns minutos
```

Confira se subiu: http://localhost:8080/health deve responder `Healthy`.

| Quem | E-mail | Senha | 2FA |
| --- | --- | --- | --- |
| Financeiro | `financeiro.demo@exemplo.invalid` | `Demo@Retirada2026` | sim |
| Gestor | `gestor.demo@exemplo.invalid` | `Demo@Retirada2026` | sim |
| Técnico | `tecnico.demo@exemplo.invalid` | `Demo@Retirada2026` | não |
| Estoque | `estoque.demo@exemplo.invalid` | `Demo@Retirada2026` | não |

- **Configure o 2FA antes da apresentação.** Entre uma vez como financeiro e uma como gestor, leia o QR code com o Google Authenticator (ou outro aplicativo autenticador) e guarde o celular por perto. Na hora, é só digitar o código.
- **Use duas janelas:** uma normal e uma anônima. Assim dá para ficar com o financeiro numa e trocar de papel na outra, sem sair e entrar o tempo todo.
- **App do técnico:**
  - **No celular:** abra `http://<ip-da-máquina>:8080` na mesma rede. O navegador só libera o GPS com HTTPS fora do `localhost`, e sem GPS a retirada não confirma. Para mostrar a retirada no celular, publique com HTTPS (por exemplo, o Nginx de homologação com certificado).
  - **Sem HTTPS:** use o modo celular do navegador (F12 → ícone de celular) em `localhost:8080` e escolha a foto `frontend/e2e/arquivos/foto-ficticia.jpg`.
- **Horário:** o botão "Ligar" só aparece de segunda a sábado, das 8h às 18h (RN10). Fora disso, a tela explica por quê, e isso também vale mostrar.

## 2. Apresentar

### Financeiro: a planilha vira ordens (RF03–RF05)

1. Entre como **financeiro** → **Importar** → escolha `ferramentas/dados-ficticios/modelo_exportacao_financeiro.xlsx` → **Ver a prévia**.
2. Mostre a prévia antes de gravar:
   - a ordem nova;
   - as linhas recusadas porque a série **já está em aberto em outra ordem** (RN11). Reenviar a mesma planilha não duplica nada;
   - os alertas de localização.
3. **Gravar 1 ordem**. A ordem nova nasce **em carência de 48 h** (RN03).

Para mostrar a outra planilha, a do financeiro (`planilha_retiradas_ficticia.xlsx`, 61 linhas): ela mostra as **colunas descartadas** (CPF, e-mail, plano, PPPoE...), que nunca são gravadas (RN04). Ela não traz os equipamentos.

### Financeiro: a ordem no painel

1. **Ordens** → filtro **Em carência** → clique no número da OS.
2. Mostre o detalhe:
   - o cliente e os telefones;
   - os itens;
   - o histórico;
   - **O que fazer**, que lista só as ações que valem agora.
3. **Liberar agora**: a ordem fica Disponível sem esperar as 48 h. Se ninguém liberar, a rotina automática libera sozinha quando vence.
4. **Registrar WhatsApp** → modelo **Aviso de retirada** → **Enviada**. O telefone passa a mostrar "Tem WhatsApp", e o contato entra no histórico (RN10). O sistema não manda mensagem: ele registra o que a pessoa mandou pelo WhatsApp oficial.
5. Conte a regra dos **10 dias sem resposta** (RN09):
   - com tentativa de contato e nenhuma resposta, a ordem encerra sozinha e vai para a cobrança;
   - sem nenhuma tentativa, ela **não** encerra e aparece no filtro **Sem contato no prazo**.

### Gestor: designar ao técnico (RF08)

Entre como **gestor** (janela anônima) → **Ordens** → a mesma OS → **Designar a um técnico** → **Técnico Demonstração** → **Designar**.

### Técnico: no celular (RF11–RF13, RN14)

1. Entre como **técnico**. A ordem aparece em **Minhas ordens**. As ordens com localização aparecem também no mapa da cidade simulada.
2. Abra a ordem e mostre:
   - o endereço;
   - o botão **Rota**, que abre o mapa do celular;
   - **Ligar**, que depois pergunta "Como foi a ligação?" (atendeu, não atendeu, caixa postal, número não existe);
   - o histórico.
3. **Iniciar retirada**:
   - **Série:** digite a da etiqueta, que aparece como "esperada". Ela confere mesmo com minúscula e espaço. Uma série diferente vira divergência e passa a **exigir a foto da etiqueta**.
   - **Fonte:** marque "Recolhida".
   - **Foto:** **Tirar foto dos equipamentos**. Ela é enviada na hora ("Enviada"), menor e sem os dados escondidos do celular (EXIF).
   - **Confirmar retirada**: GPS e hora entram sozinhos.
4. Se quiser, mostre a **visita sem sucesso**: com o motivo, a ordem volta para a fila; na 3ª, encerra sem retirada (RN09).

### Estoque: conferir (RF14, RN08)

1. Entre como **estoque** → **Conferir** → busque pelo número da OS ou pela série.
2. Leia a série e escolha **Fechar como Conferida**. Com série diferente ou item faltando, a ordem continua aberta, com a divergência registrada.
3. Para mostrar a **devolução na loja** (RF15), use outra ordem:
   - busque **5101** → **Registrar devolução**;
   - deixe marcada só a **Fonte 12V** → **Confirmar devolução de 1 item**;
   - a ordem continua com o técnico, só com a ONU e o roteador;
   - com tudo marcado, a ordem encerra.

### Financeiro: baixa no HubSoft (RF19)

**Ordens** → filtro **Para dar baixa** → a OS → veja as **Fotos da retirada**, que abrem inteiras ao clicar → **Marcar baixa feita**. A ordem sai da lista.

### Fechamento

- O **histórico** da ordem conta tudo: quem fez (só o papel), o quê e quando. O registro é só de acréscimo; ninguém apaga.
- **Gestão de usuários** (gestor): cadastro por convite, revogação na hora e 2FA obrigatório para gestor e financeiro.

## 3. Se algo sair do roteiro

| Sintoma | O que fazer |
| --- | --- |
| "Muitas tentativas" no login | Espere 1 minuto. É o limite contra força bruta (ADR-010). |
| O código do 2FA não entra | Confira se o relógio do celular está certo. Cada código vale uma vez só: espere o próximo. |
| A foto não envia | Toque em **Tentar de novo** na própria foto; as outras não se perdem. Se continuar, veja `docker compose logs fotos api`. |
| Não aparece o botão Ligar | Fora da janela de contato (seg–sáb, 8h–18h) ou telefone fora do padrão. |
| Quer começar do zero | `docker compose down -v && docker compose up --build`. **Apaga tudo**: banco, fotos e chaves. O 2FA precisa ser configurado de novo. |

## 4. Ensaio automático (opcional)

O mesmo roteiro roda sozinho no navegador e guarda uma captura de cada passo em `frontend/ensaio-resultado/`:

```bash
docker compose down -v && docker compose up --build -d   # banco limpo: o ensaio configura o 2FA dos usuários
cd frontend && npm ci && npx playwright install chromium
ENSAIO_URL=http://localhost:8080 npm run ensaio
```

**Depois do ensaio, recarregue o banco** (`docker compose down -v && docker compose up -d`). O ensaio deixa o 2FA do financeiro e do gestor amarrado a ele mesmo, e você não conseguiria entrar com esses usuários.
