# ADR-010 · Autenticação e autorização

**Status:** Aceita

## Contexto

RF01, RF02, RNF03 e RNF04: perfis diferentes, revogação imediata, 2FA para gestor e financeiro, e técnico vendo só as próprias ordens.

## Decisão

- Sessão por cookie HttpOnly, sem JWT: PWA e API estão no mesmo endereço, e o servidor confere o usuário a cada requisição, então revogar corta na hora.
- Senha com Argon2id (parâmetros da OWASP); bloqueio por 15 min após 5 erros e limite de tentativas por IP.
- 2FA por aplicativo autenticador (TOTP) para gestor e financeiro.
- Cadastro por convite: o usuário fica pendente até definir a senha pelo link, que vale 48 h e serve uma vez.
- Perfil e dono do registro conferidos no servidor; auditoria de entrar, errar senha, sair e revogar.

## Alternativas descartadas

- JWT no navegador: um XSS rouba o token, e revogar só vale quando ele expira.
- ASP.NET Core Identity: tabelas e fluxo próprios, que brigam com a tabela `usuario` do modelo.

## Consequências

- Migração nova com colunas de senha, bloqueio e 2FA, e a tabela de convites.
- Proteção contra CSRF: cookie SameSite e cabeçalho próprio nos comandos.
