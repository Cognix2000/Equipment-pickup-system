# ADR-012 · Chave de acesso (passkey) no lugar do aplicativo autenticador

**Status:** Aceita. Complementa a [ADR-010](0010-autenticacao.md): o resto da autenticação continua igual.

## Contexto

Pela ADR-010, gestor e financeiro só entram com o código de um aplicativo autenticador (RNF03). Na prática, isso obriga a pessoa a instalar um aplicativo no celular e ler um QR code, e é aí que o primeiro acesso trava.

Precisamos de outras formas de entrar, sem perder o segundo fator:
- sem instalar nada;
- sem ninguém se cadastrar sozinho (RF02);
- sem abrir brecha para phishing.

## Decisão

- **Chave de acesso (passkey, WebAuthn)** com a biblioteca Fido2NetLib. O servidor guarda só a chave **pública**; a privada fica no aparelho ou no cofre do Google, da Apple ou da Microsoft.
- **A chave vale pelos dois fatores.** O servidor exige a verificação do usuário (digital, rosto ou PIN do aparelho) e recusa a resposta sem ela. Por isso, gestor e financeiro entram só com a chave.
- **Entrar sem digitar o e-mail.** A chave é residente, e o aparelho mostra as chaves que tem para o sistema. O desafio fica num cookie HttpOnly cifrado, vale 5 minutos e serve uma vez.
- **Primeiro acesso de gestor e financeiro:**
  - a tela oferece primeiro "Cadastrar a digital ou o rosto";
  - o aplicativo autenticador continua como opção.
- **Na etapa do código**, quem só sabe a senha **não** cadastra chave. Assim, a senha vazada não troca o aplicativo de alguém por uma chave do atacante.
- **Gestor ou financeiro com chave e sem aplicativo** que digitam a senha são mandados confirmar com a chave. A senha sozinha não abre sessão.
- **Minha conta** (`/conta`), para todos os papéis:
  - listar as próprias chaves;
  - cadastrar outro aparelho;
  - remover uma chave, com confirmação.
- **O bloqueio por senha errada não barra a chave.** Ele existe contra quem tenta adivinhar a senha, e a chave não se adivinha. Usuário revogado ou pendente não entra de jeito nenhum.
- **"Zerar 2FA"** do gestor apaga o aplicativo **e** todas as chaves. É o caminho de quem perdeu o celular.
- **A chave fica presa ao domínio.** O domínio e a origem vêm de `Acesso:ChaveDeAcesso:Dominio` e `Acesso:ChaveDeAcesso:Origens`; o compose de produção monta os dois a partir do `DOMINIO`. Em outro endereço a chave não funciona, e é isso que barra o phishing.

## Alternativas descartadas

- **Tirar o 2FA de gestor e financeiro:** eles cancelam ordens e veem dados de clientes (RNF03).
- **Código por SMS:** custa por mensagem e é o segundo fator mais fraco (troca de chip).
- **Implementar o WebAuthn na mão:** CBOR, COSE e os formatos de atestado são fáceis de errar. A biblioteca é mantida e cobre os aparelhos atuais.

## Consequências

- Tabela nova `chave_de_acesso` (migração `0004`), com `credencial_id` único.
- **A chave só funciona com HTTPS** (ou em `localhost`, no desenvolvimento). Trocar o domínio de produção invalida as chaves já cadastradas; o aplicativo e a senha continuam valendo.
- **Testes:**
  - a API passa pela verificação criptográfica de verdade, com um autenticador de teste em C#. Ficam cobertos assinatura falsa, desafio repetido, outra origem e falta da digital;
  - o e2e usa o autenticador virtual do Chromium.
- **Próximos passos, em PRs separados:**
  - código por e-mail;
  - entrar com Microsoft 365 e com Google. Esses dois só valem para quem já foi convidado, e a conta é ligada pelo identificador do provedor, nunca pelo e-mail.
