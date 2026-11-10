# ADR-014 · Resgate do técnico terceiro por PIX, pelo Asaas

**Status:** Aceita. Complementa o repasse ao técnico (PR #36).

## Contexto

Cada retirada conferida pelo estoque vale R$ 5,00 para quem retirou. Até aqui, o sistema só fazia a conta: o financeiro pagava por fora e registrava o pagamento.

O Pedro decidiu (03/11/2026):
- o técnico **terceiro** (PJ) recebe como um entregador de aplicativo. Ele aperta "Resgatar" e o dinheiro cai por PIX, na hora;
- o técnico **CLT** recebe na folha, no fim do mês, junto com o salário. No app, ele só acompanha;
- só entra no resgate o que o estoque já conferiu;
- o pagamento precisa ter prova que não depende da palavra de ninguém: o ID de Fim a Fim do Banco Central e o comprovante.

Para o CLT, o valor por retirada faz parte da remuneração (encargos como INSS e FGTS). Pagar fora da folha traria risco trabalhista. A confirmação final é da contabilidade.

## Decisão

- **O técnico ganha o campo `vinculo`** (`clt` ou `terceiro`), definido pelo gestor no cadastro e mudável depois. O papel continua `tecnico` para os dois: o trabalho e as telas são os mesmos. Técnico sem vínculo gravado conta como CLT.
- **Gateway: Asaas.** Ele envia PIX para chave de terceiros pela API (`POST /v3/transfers`), avisa o resultado por webhook (`TRANSFER_DONE`, `TRANSFER_FAILED`, `TRANSFER_CANCELLED`) com o `endToEndIdentifier` e o `transactionReceiptUrl`, e tem a **validação antes de pagar**: o Asaas pergunta ao nosso servidor antes de soltar cada PIX.
- **Porta `IGatewayDePagamento`**, com três adaptadores:
  - `asaas` (produção);
  - `simulado` (homologação e desenvolvimento: o PIX "cai" na hora e nenhum dinheiro sai; a API recusa subir com ele em produção);
  - `desligado` (padrão, até a conta existir).
- **Chave PIX só vale depois que o financeiro aprova.** Quem entrar na conta do técnico não consegue trocar a chave para a dele. A chave nova fica pendente, e o resgate continua indo para a aprovada até lá.
- **O resgate em quatro passos:**
  1. o técnico aperta "Resgatar";
  2. numa transação, o sistema grava o resgate ("solicitado") e **reserva** os créditos livres;
  3. fora da transação, pede o PIX ao gateway. O id do resgate vai em `externalReference`;
  4. o aviso do gateway marca "pago" (vira um `repasse_pagamento`, com o ID de Fim a Fim e o comprovante) ou "falhou" (os créditos voltam ao saldo).
- **Um resgate em andamento por técnico** (índice único). Clique duplo não manda dois PIX.
- **Validação antes de pagar:** o servidor só aprova uma transferência que bate com um resgate em andamento:
  - com o mesmo valor;
  - com o mesmo id de transferência, quando o sistema já o conhece;
  - com a chave de destino aprovada, quando o Asaas a manda. Por isso, **a conta do Asaas é só dos resgates**: qualquer outra transferência dela é recusada.
- **Os avisos do Asaas** chegam em `/api/webhooks/asaas/{validacao,eventos}`, sem sessão e sem o cabeçalho anti-CSRF. Quem prova a origem é o token `asaas-access-token`, comparado em tempo constante. Eles ficam fora do limite de requisições.
- **Conciliação na rotina de 5 min:**
  - resgate "solicitado" há mais de 10 min vira falha, porque o pedido ao gateway não terminou;
  - "enviado" há mais de 30 min sem aviso é consultado no gateway.
- **Resgate mínimo** de R$ 20,00 (`Pagamentos:ResgateMinimoCentavos`), porque cada PIX tem custo no gateway.
- **Para o CLT**, a tela de repasses do financeiro mostra a **folha do mês**, com as retiradas conferidas no mês por técnico CLT. O financeiro leva o valor à folha e registra o pagamento como antes.

## Alternativas descartadas

- **AbacatePay:** envia PIX a terceiros e devolve o comprovante. Mas a documentação (novembro de 2026) não mostra o ID de Fim a Fim do envio nem a validação antes de pagar. O Pedro escolheu o Asaas se isso não se confirmasse. Trocar depois é escrever outro adaptador da mesma porta.
- **Efí (Gerencianet):** devolve o ID de Fim a Fim, mas exige certificado digital (mTLS) e mais configuração.
- **Pagar o CLT pelo PIX também:** risco trabalhista e de encargos (ver Contexto).
- **Pagamento automático toda semana, sem o técnico pedir:** o Pedro quis o modelo de saque, em que o técnico aperta "Resgatar".
- **Marcar "pago" pela resposta do pedido:** a resposta do Asaas chega pendente. Quem diz que o dinheiro caiu é o aviso, com o ID de Fim a Fim.

## Consequências

- Migração `0007`:
  - `usuario.vinculo`;
  - as tabelas `chave_pix` e `resgate`;
  - `repasse_credito.resgate_id`, a reserva.
- O pagamento manual do financeiro e a lista de repasses deixam de fora o que está reservado num resgate.
- O CPF ou CNPJ do técnico terceiro pode ficar gravado como chave PIX. É dado do técnico, não do cliente; a tela dele mostra a chave mascarada, e só o financeiro vê inteira.
- A produção precisa:
  - da conta do Asaas no CNPJ da Netwise, com saldo;
  - da chave da API e do token dos webhooks no `.env`;
  - dos dois webhooks configurados no painel do Asaas (docs/implantacao.md, "Resgate por PIX (Asaas)").
- **A conferir no ambiente de testes do Asaas antes de ligar:**
  - o formato que ele aceita para a chave de celular (o sistema manda `+55DDNNNNNNNNN`);
  - os endereços da API (produção `https://api.asaas.com`, testes `https://api-sandbox.asaas.com`).
