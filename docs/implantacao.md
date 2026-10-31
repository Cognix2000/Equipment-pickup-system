# Implantação em produção

A produção roda no servidor com Docker, atrás do **Caddy**, que cuida do HTTPS automático com Let's Encrypt. As imagens da API e do app chegam **prontas do GHCR**, publicadas pelo workflow "Publicar imagens", então o servidor não compila nada.

**A produção só recebe versão com tag** (`vX.Y.Z`, ADR-006). O merge na `main` não chega aos usuários: quem decide é você, ao lançar uma versão (veja [Lançar uma versão](#lançar-uma-versão)). A homologação é o `docker-compose.yml` sozinho, na sua máquina, com a `main`.

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

3. **Configuração.** O script cria o `.env` com senhas aleatórias fortes, permissão só para o dono, o domínio e a versão:
   ```bash
   sh ferramentas/criar-env.sh producao pedro.devioslab.com.br v0.1.0
   ```
   Não existe arquivo de exemplo no repositório, e o `.env` nunca vai para o Git. Guarde uma cópia dele num cofre de senhas: sem ela, o backup do banco não serve.

   | Variável | O que é |
   | --- | --- |
   | `VERSAO` | A versão lançada que roda (tag `vX.Y.Z`) |
   | `DOMINIO` | O domínio com DNS para o servidor (HTTPS) |
   | `BANCO_SENHA_DONO`, `BANCO_SENHA_API` | Senhas do banco: valem desde a primeira subida, **não troque depois** |
   | `FOTOS_CHAVE`, `FOTOS_SEGREDO` | Acesso da API ao armazenamento das fotos |
   | `WEB_PORTA` | `127.0.0.1:8080`: o app só sai pelo Caddy |
   | `IMPORTACAO_MODO_SIMULACAO` | `false`: só telefone real (RN05) |
   | `OTEL_ENDPOINT` | Destino da telemetria (vazio = nenhum) |
   | `VALOR_POR_RETIRADA_CENTAVOS` | Repasse ao técnico por retirada conferida |

4. **Subir.** A `VERSAO` do `.env` precisa ser uma versão já lançada, como `v0.1.0` (veja [Lançar uma versão](#lançar-uma-versão)):
   ```bash
   sh deploy/atualizar.sh
   ```
   O Caddy emite o certificado no primeiro acesso a `https://<DOMINIO>`. Para acompanhar: `docker compose -f docker-compose.yml -f docker-compose.producao.yml logs -f proxy`.

5. **Primeiro gestor.** Em produção não há usuários fictícios: o primeiro gestor entra por convite.
   ```bash
   docker compose -f docker-compose.yml -f docker-compose.producao.yml run --rm migrador \
     convidar-gestor --nome "Nome do Gestor" --email gestor@empresa --url https://<DOMINIO>
   ```
   O link vale 48 h e uma vez só. A partir daí, o gestor cadastra os outros usuários pela tela de gestão.

## Antes dos dados reais: zerar o teste

Enquanto o servidor tiver só dado de teste (usuários e ordens fictícias), zere tudo antes do primeiro dado real. Ordens e históricos nunca são apagados pela aplicação (RN12), então o teste não pode ficar misturado com o real. O comando abaixo **apaga** o banco, as fotos, as chaves e o certificado:

```bash
cd ~/retirada
docker compose -f docker-compose.yml -f docker-compose.producao.yml down -v
sh deploy/atualizar.sh
```

Depois, crie o primeiro gestor real, como no item 5 de "Primeira vez".

## Lançar uma versão

O caminho de um merge até a produção, sempre com uma decisão sua no meio:

1. **Merges na `main`**, cada um com o CI verde.
2. **Homologação na sua máquina:**
   ```bash
   git switch main && git pull && docker compose up --build
   ```
   Confira o que mudou, seguindo o [roteiro de demonstração](roteiro-de-demonstracao.md).
3. **Crie a versão no GitHub:**
   1. Vá em **Releases › Draft a new release**.
   2. Em **Choose a tag**, digite a versão nova (ex.: `v0.2.0`) e confirme **Create new tag on publish**, com **Target: main**.
   3. Clique em **Generate release notes**: a lista dos PRs sai sozinha.
   4. Clique em **Publish release**.

   Ou, pelo terminal:
   ```bash
   git tag v0.2.0 origin/main && git push origin v0.2.0
   ```
4. **Espere** o workflow "Publicar imagens" da tag terminar verde (aba Actions). Ele publica `retirada-api:v0.2.0` e `retirada-web:v0.2.0`.
5. **No servidor:**
   ```bash
   cd ~/retirada
   sed -i 's/^VERSAO=.*/VERSAO=v0.2.0/' .env
   sh deploy/atualizar.sh
   ```

O script faz cinco coisas:
- confere se a `VERSAO` é uma tag (recusa `main`);
- leva o código do servidor para a **mesma tag**: compose, Caddyfile e scripts andam junto com as imagens;
- baixa as imagens dessa versão e sobe o migrador, que aplica as migrações novas antes da API;
- troca os contêineres;
- apaga as imagens antigas, porque o disco é pequeno.

**Qual número usar** (SemVer, ADR-006):

| Mudança | Exemplo |
| --- | --- |
| Só correção | `v0.2.0` → `v0.2.1` |
| Funcionalidade nova | `v0.2.1` → `v0.3.0` |
| Início com dados reais | `v1.0.0` |

### Voltar para a versão anterior

```bash
sed -i 's/^VERSAO=.*/VERSAO=v0.1.0/' .env && sh deploy/atualizar.sh
```

As migrações do banco só andam para a frente. Voltar o código funciona quando a versão nova só **acrescentou** tabelas e colunas, que é o padrão do projeto. Antes de lançar uma versão que mude ou apague uma coluna, faça backup do banco.

### Primeira vez com versão (servidor que estava na `main`)

Depois do merge que trouxe esta regra, lance a `v0.1.0` (passo 3 acima) e rode, uma vez:

```bash
cd ~/retirada
git fetch --force origin && git checkout --detach origin/main     # traz o script novo (funciona depois da limpeza do histórico)
grep -q '^VERSAO=' .env || echo 'VERSAO=' >> .env
sed -i 's/^VERSAO=.*/VERSAO=v0.1.0/' .env
sed -i '/^MODO_DEMONSTRACAO=/d; /^SENHA_DEMONSTRACAO=/d' .env      # o modo demonstração saiu
sh deploy/atualizar.sh
```

Nas próximas vezes, o próprio script leva o código para a tag.

## O que guardar no backup

Estes quatro itens fazem o sistema voltar inteiro:
- o **banco**;
- o volume **`fotos`**, que é a prova das retiradas (RN14);
- o volume **`chaves`**: sem ele, todos saem da sessão e o 2FA por aplicativo precisa ser configurado de novo (as chaves de acesso ficam no banco e continuam valendo);
- o **`.env`**.

O volume `caddy_dados` só evita emitir o certificado de novo.

## Monitor de disponibilidade (fora do servidor)

O monitor fica **fora** do servidor de propósito. Um monitor instalado no próprio servidor (como o Uptime Kuma) para junto com ele e não avisa justamente quando o servidor cai, além de gastar a pouca RAM que ele tem.

O `/health` responde **200** quando a API e o banco estão bem e **503** quando o banco falha. Basta conferir o código da resposta.

**Recomendado: StatusCake, plano gratuito.** Ele permite uso comercial, com 10 monitores a cada 5 minutos e alerta por e-mail. O UptimeRobot gratuito **não serve**: desde 2024, ele só vale para projeto pessoal ou sem fins lucrativos.

1. Crie a conta em statuscake.com, de preferência com um e-mail da equipe e não um pessoal.
2. Em **Uptime › New Uptime Test**, preencha:
   - **Test type:** HTTP(S);
   - **Website URL:** `https://pedro.devioslab.com.br/health`;
   - **Check rate:** 5 minutos;
   - **Confirmation:** 2 servidores, para não avisar por um soluço da rede.
3. Em **Contact Groups**, cadastre os e-mails de quem deve saber. Para receber no celular, use Telegram, Discord ou Slack.
4. Crie um segundo teste para `https://pedro.devioslab.com.br/`. Ele confere se o app (PWA) abre e se o certificado HTTPS está válido.
5. Para testar o alerta, pare a API por 15 minutos fora do horário de uso e confira se o e-mail chega:
   ```bash
   docker compose -f docker-compose.yml -f docker-compose.producao.yml stop api
   ```
   Depois, suba de novo:
   ```bash
   docker compose -f docker-compose.yml -f docker-compose.producao.yml start api
   ```

**Alternativa sem conta externa:** o Uptime Kuma, instalado em **outra** máquina que fique ligada (outro servidor ou um mini PC da empresa), apontando para os mesmos dois endereços.

## Valor do repasse ao técnico

Cada retirada conferida pelo estoque gera R$ 5,00 para o técnico que retirou. Para mudar o valor, ponha `VALOR_POR_RETIRADA_CENTAVOS` no `.env`, em centavos (ex.: `700` = R$ 7,00), e rode `sh deploy/atualizar.sh`. O valor novo vale só para as próximas conferências: o que já foi creditado não muda.

**Não edite os arquivos do repositório no servidor** (compose, Caddyfile): o deploy leva o código para a tag e recusa se houver mudança local. O que muda por servidor fica no `.env`.

## Chave de acesso (digital ou rosto)

A chave de acesso (ADR-012) fica presa ao `DOMINIO` do `.env`: o compose de produção passa o domínio e a origem `https://<DOMINIO>` para a API. Se o domínio mudar, as chaves cadastradas param de valer, e cada pessoa entra com a senha (e o aplicativo, se tiver) e cadastra de novo em **Minha conta**. Quem perdeu o celular pede ao gestor **Zerar 2FA e chaves**.
