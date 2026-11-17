# ADR-015 · Login flexível e sessão longa

**Status:** Aceita. Revê a decisão de 2FA obrigatório da [ADR-010](0010-autenticacao.md) e o fluxo de primeiro acesso da [ADR-012](0012-chave-de-acesso.md).

## Contexto

Pela ADR-010, gestor e financeiro só entravam com o segundo fator (RNF03). A ADR-012 trouxe a chave de acesso como alternativa ao aplicativo autenticador, mas a obrigatoriedade continuou: o primeiro login desses dois papéis forçava o passo "configurar" antes de entrar.

Isso criou dois problemas na prática:

- **No desktop, sem leitor biométrico**, só o TOTP sobra — e o gestor que não quer instalar aplicativo novo trava.
- **A sessão durava 12 horas** com rolagem (ADR-010): a pessoa entra de manhã e, no dia seguinte, precisa entrar de novo. O financeiro reclamou.

Pedro decidiu em nov/2026 afrouxar as duas regras, sem tirar as opções que já existem.

## Decisão

- **O segundo fator passa a ser opcional**, para todos os papéis. Quem quer configura TOTP ou chave de acesso pelo painel; quem não quer, entra só com a senha. A senha segue com os mesmos requisitos (Argon2id, mínimo 12 caracteres, bloqueio por tentativas).
- **O Entrar.cs decide se pede segundo fator** a partir do que o próprio usuário configurou, não do papel:
  - `Tem2FA` → etapa `codigo` (código do aplicativo);
  - sem TOTP → etapa `pronto` direto, mesmo que o usuário tenha chave de acesso cadastrada.
- **A chave de acesso NÃO é segundo fator.** Em nov/2026 o retorno `etapa: 'chave'` foi tirado: a chave vira caminho de entrada próprio em `/chave/entrar` (passwordless, botão "Entrar com a digital ou o rosto" na tela de login). Motivo: no desktop sem biometria, obrigar a chave após a senha deixava o usuário preso. A chave continua como opção escolhida pelo usuário, seja para entrar sem digitar senha, seja como atalho no celular.
- **Formas de entrar continuam todas disponíveis**: senha, TOTP, chave de acesso (passwordless). O Microsoft 365 e o Google entram em PR separado quando o admin do M365 e o console do Google Cloud estiverem configurados.
- **Sessão passa de 12 h para 30 dias**, mantendo `SlidingExpiration`. Cada uso do sistema renova a janela. O cookie continua `HttpOnly`, `SameSite=Strict`, `Secure` em HTTPS, cifrado pelo Data Protection.

## Alternativas descartadas

- **Manter 2FA obrigatório só para gestor:** complica o cadastro de novos usuários e não resolve o pedido do financeiro.
- **Marcar por usuário na tabela (`requer_2fa bool`):** mais código, mais UI de admin, sem ganho prático enquanto quem decide é o próprio Pedro.
- **Sessão "para sempre":** sem rolagem, um cookie roubado fica válido indefinidamente. 30 dias com rolagem é o padrão de sistemas corporativos sérios (ex.: Google Workspace).

## Consequências

- `Usuario.Exige2FA` sai — quem decide é `Entrar.cs` a partir de `Tem2FA`.
- O painel do usuário (`/conta`) continua sendo o lugar para ligar/desligar 2FA — a tela de login só pede quando já está ligado.
- Testes que seguravam o fluxo "entrar com 2FA obrigatório" (helper `EntrarCom2FAAsync`) agora são usados só pelos testes específicos de 2FA.
- O componente `PassoChave` e o case `etapa === 'chave'` na tela de login foram removidos; a chave vira só o atalho "Entrar com a digital ou o rosto" em cima do formulário de senha.
- Para o técnico em campo, que já entrava só com a senha, nada muda.

Sem migração de banco: `TotpAtivadoEm` já era nullable e não mudou de significado.
