# Implantação em produção

A produção roda no servidor com Docker, atrás do **Caddy**, que cuida do HTTPS automático com Let's Encrypt. As imagens da API e do app chegam **prontas do GHCR**, publicadas pelo workflow "Publicar imagens" a cada merge na `main`, então o servidor não compila nada. A homologação continua sendo o `docker-compose.yml` sozinho.

```
internet ──443/80──▶ proxy (Caddy) ──/api, /health──▶ api ──▶ banco (PostGIS)
                          └──────── resto ──────────▶ web     └──▶ fotos (SeaweedFS)
```

Só as portas 80 e 443 ficam abertas para a internet. O banco (5432) e o app (8080) escutam em `127.0.0.1`, e as fotos ficam só dentro da rede do Docker.

## Requisitos do servidor

- Ubuntu 24.04 com Docker e o plugin `docker compose`, instalados do repositório oficial da Docker.
- 1 GB de RAM com 1 GB de swap atende o início: o sistema usa uns 400 MB.
- O domínio do `.env` (registro DNS tipo A) apontando para o IP do servidor.
- As portas 80 e 443 abertas no firewall. A 80 é necessária para emitir e renovar o certificado.

**Atenção:** o Docker publica portas por fora do `ufw`. Por isso o compose de produção liga em `127.0.0.1` tudo o que não deve sair da máquina.

## Primeira vez

1. **Acesso ao código.** Crie uma deploy key, só leitura, para o servidor clonar o repositório privado:
   - **No servidor:**
     ```bash
     ssh-keygen -t ed25519 -f ~/.ssh/retirada_deploy -N "" -C "deploy@servidor"
     cat ~/.ssh/retirada_deploy.pub
     ```
   - **No GitHub:** em Settings › Deploy keys › Add deploy key, cole a chave pública e **deixe sem** "Allow write access".
   - **De volta ao servidor:**
     ```bash
     printf 'Host github.com\n  IdentityFile ~/.ssh/retirada_deploy\n  IdentitiesOnly yes\n' >> ~/.ssh/config
     git clone git@github.com:Cognix2000/Equipment-pickup-system.git retirada && cd retirada
     ```

2. **Acesso às imagens.** Crie um token clássico do GitHub só com `read:packages` e entre no GHCR. O token fica guardado no servidor:
   ```bash
   docker login ghcr.io -u <seu-usuario-github>   # cole o token quando pedir a senha
   ```

3. **Configuração.** Gere um valor forte para cada "troque-…" com `openssl rand -hex 24`:
   ```bash
   cp .env.producao.example .env && chmod 600 .env && nano .env
   ```
   Guarde uma cópia do `.env` num cofre de senhas. Sem ela, o backup do banco não serve.

4. **Subir:**
   ```bash
   deploy/atualizar.sh
   ```
   O Caddy emite o certificado no primeiro acesso a `https://<DOMINIO>`. Para acompanhar: `docker compose -f docker-compose.yml -f docker-compose.producao.yml logs -f proxy`.

5. **Primeiro gestor.** Em produção não há usuários fictícios: o primeiro gestor entra por convite.
   ```bash
   docker compose -f docker-compose.yml -f docker-compose.producao.yml run --rm migrador \
     convidar-gestor --nome "Nome do Gestor" --email gestor@empresa --url https://<DOMINIO>
   ```
   O link vale 48 h e uma vez só. A partir daí, o gestor cadastra os outros usuários pela tela de gestão.

## Modo demonstração (antes dos dados reais)

Antes de entrar com dados reais, o servidor pode rodar com os **usuários de teste**, um por papel, e as **ordens fictícias**, para mostrar o produto:

| Papel | E-mail | 2FA |
| --- | --- | --- |
| Gestor | `gestor.demo@exemplo.invalid` | sim |
| Financeiro | `financeiro.demo@exemplo.invalid` | sim |
| Técnico | `tecnico.demo@exemplo.invalid` | não |
| Estoque | `estoque.demo@exemplo.invalid` | não |

A senha dos quatro é a `SENHA_DEMONSTRACAO` do `.env`, e não a do README. O site está aberto na internet.

1. **Ligar:**
   ```bash
   cd ~/retirada
   grep -q '^MODO_DEMONSTRACAO=' .env || echo 'MODO_DEMONSTRACAO=false' >> .env    # .env criado antes desta opção
   grep -q '^SENHA_DEMONSTRACAO=' .env || echo 'SENHA_DEMONSTRACAO=' >> .env
   sed -i 's/^MODO_DEMONSTRACAO=.*/MODO_DEMONSTRACAO=true/' .env
   sed -i "s|^SENHA_DEMONSTRACAO=.*|SENHA_DEMONSTRACAO=$(openssl rand -base64 18)|" .env
   grep '^SENHA_DEMONSTRACAO=' .env       # anote a senha no gerenciador de senhas
   sh deploy/atualizar.sh
   ```
2. **Logo em seguida, entre como gestor e como financeiro e configure o 2FA dos dois.** Até isso acontecer, quem souber a senha poderia configurar o 2FA no lugar de vocês.
3. A senha só é definida para usuário fictício que ainda não tem senha. Trocar a `SENHA_DEMONSTRACAO` depois não muda nada, a não ser que você saia do modo e ligue de novo.

### Sair do modo demonstração

Ordens e históricos nunca são apagados pela aplicação (RN12). Por isso, o teste é **zerado inteiro** antes do primeiro dado real. O comando abaixo **apaga** o banco, as fotos, as chaves e o certificado:

```bash
cd ~/retirada
docker compose -f docker-compose.yml -f docker-compose.producao.yml -f docker-compose.demonstracao.yml down -v
sed -i 's/^MODO_DEMONSTRACAO=.*/MODO_DEMONSTRACAO=false/; s/^SENHA_DEMONSTRACAO=.*/SENHA_DEMONSTRACAO=/' .env
sh deploy/atualizar.sh
```

Depois, crie o primeiro gestor real, como no item 5 de "Primeira vez".

## Atualizar

Depois de um merge na `main`, espere o workflow "Publicar imagens" terminar e rode no servidor:

```bash
deploy/atualizar.sh
```

O script faz quatro coisas:
- baixa o repositório, com o Caddyfile e o compose, e as imagens da `VERSAO` do `.env`;
- sobe o migrador, que aplica as migrações novas antes da API;
- troca os contêineres;
- apaga as imagens antigas, porque o disco é pequeno.

Para fixar uma versão, crie a tag `vX.Y.Z` na `main`, espere a publicação e coloque `VERSAO=vX.Y.Z` no `.env`.

## O que guardar no backup

Estes quatro itens fazem o sistema voltar inteiro:
- o **banco**;
- o volume **`fotos`**, que é a prova das retiradas (RN14);
- o volume **`chaves`**: sem ele, todos saem da sessão e o 2FA precisa ser configurado de novo;
- o **`.env`**.

O volume `caddy_dados` só evita emitir o certificado de novo.
