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

6. **Vários usuários de uma vez, com senha definida** (opcional). Se o gestor quiser entrar como cada papel para conferir o sistema, dá para pular o convite:
   ```bash
   SENHA_INICIAL='SuaSenhaAqui123!' sh ferramentas/criar-usuarios-netwise.sh   # cria financeiro, estoque e 2 técnicos
   ```
   Ou um por vez:
   ```bash
   docker compose -f docker-compose.yml -f docker-compose.producao.yml run --rm migrador \
     criar-usuario --nome "Financeiro" --email financeiro@empresa --papel financeiro --senha 'SuaSenhaAqui123!'
   docker compose -f docker-compose.yml -f docker-compose.producao.yml run --rm migrador \
     criar-usuario --nome "Técnico CLT" --email tecnico@empresa --papel tecnico --senha 'SuaSenhaAqui123!' --vinculo clt
   ```
   A senha segue a política do login (mínimo 12 caracteres, não pode conter o começo do e-mail). Gestor e financeiro **ainda precisam cadastrar o 2FA** no primeiro login (digital, rosto ou aplicativo autenticador).

## Antes dos dados reais: zerar o teste

Enquanto o servidor tiver só dado de teste (usuários e ordens fictícias), zere tudo antes do primeiro dado real. Ordens e históricos nunca são apagados pela aplicação (RN12), então o teste não pode ficar misturado com o real. Há dois caminhos:

### Opção A — Zerar mantendo o gestor que você já cadastrou (recomendada)

Use quando o gestor real já está no banco (por ter se cadastrado em homologação) e você quer manter a conta dele intacta, inclusive as chaves de acesso. O script `db/limpar-para-producao.sql` apaga toda a parte operacional (clientes, ordens, equipamentos, importações, histórico, parceiros, repasses, auditoria, convites, telefones, chaves pix) e todos os demais usuários — só o `parametro` e o gestor informado ficam.

Rode no host de produção:

```bash
cd ~/retirada
docker compose cp db/limpar-para-producao.sql banco:/tmp/limpar.sql
docker compose exec -T banco psql -U retirada_dono -d retirada \
    -v gestor_email='pedromendes211205@gmail.com' \
    -f /tmp/limpar.sql
```

O script está em transação e só confirma se o gestor existir. Rodar de novo é inofensivo (tudo já estará vazio). As fotos no SeaweedFS continuam no bucket; se quiser apagá-las junto, zere o volume do seaweed antes:

```bash
docker compose stop seaweed && docker volume rm retirada_seaweed_data && docker compose up -d seaweed
```

### Opção B — Zerar banco, fotos, chaves e certificado (reset completo)

Use quando quer começar do zero e recadastrar o gestor pelo fluxo de convite. O comando abaixo **apaga** o banco, as fotos, as chaves e o certificado:

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
   Use sempre `vX.Y.Z`, sem ponto depois do `v`. Se uma tag sair como `v.X.Y.Z` por engano (como a `v.0.3.0`), o deploy ainda a aceita: o `deploy/atualizar.sh` procura `v.X.Y.Z` quando `vX.Y.Z` não existe e grava no `.env` a tag que achou.
4. **Espere** o workflow "Publicar imagens" da tag terminar verde (aba Actions). Ele publica `retirada-api:v0.2.0` e `retirada-web:v0.2.0`.
5. **O deploy anda sozinho** quando a [implantação automática](#implantação-automática) está configurada: o workflow "Implantar na produção" entra em cena, pede sua aprovação no Environment `producao` e atualiza o servidor. Sem ela, no servidor:
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

### Implantação automática

Depois de configurada, publicar uma release no GitHub já atualiza o servidor — você aprova no painel do GitHub e pronto. Mantém a regra do ADR-006: só tag entra em produção.

O workflow **"Implantar na produção"** usa um **self-hosted runner** no próprio servidor. O SSH do servidor fica fechado para a internet (só via VPN WireGuard), então o GitHub Actions não entra por fora: o agente roda de dentro e executa o `atualizar.sh` local.

#### 1. Instale o agente no servidor (uma vez só)

1. No GitHub, vá em **Settings › Actions › Runners › New self-hosted runner**. Escolha **Linux / x64**.

2. O painel mostra uma sequência de comandos com um **token de registro** (vale só uma vez, por poucas horas). Rode-os no servidor **como `cladmin`**, não como root. O conjunto pedido é parecido com:
   ```bash
   mkdir ~/actions-runner && cd ~/actions-runner
   curl -o actions-runner-linux-x64-X.Y.Z.tar.gz -L https://github.com/actions/runner/releases/download/vX.Y.Z/actions-runner-linux-x64-X.Y.Z.tar.gz
   tar xzf actions-runner-linux-x64-X.Y.Z.tar.gz
   ```

3. Na hora do `./config.sh`, use os rótulos abaixo (o workflow procura por `producao`):
   ```bash
   ./config.sh --url https://github.com/Cognix2000/Equipment-pickup-system \
               --token <TOKEN-DO-PAINEL> \
               --labels producao \
               --name pedro \
               --work _work \
               --unattended
   ```

4. **Rodar como serviço** (para o agente voltar sozinho após reboot):
   ```bash
   sudo ./svc.sh install cladmin
   sudo ./svc.sh start
   sudo ./svc.sh status   # confirma que está "active (running)"
   ```

5. No **GHCR**, o `atualizar.sh` precisa puxar as imagens privadas. Dê ao Docker do servidor o login do GHCR, uma vez só (como `cladmin`):
   ```bash
   gh auth login                         # ou: echo "$TOKEN" | docker login ghcr.io -u <seu-usuario> --password-stdin
   gh auth token | docker login ghcr.io -u <seu-usuario> --password-stdin
   ```
   Isso grava `~/.docker/config.json`; o agente usa daqui em diante.

#### 2. Crie o Environment `producao` no GitHub

Em **Settings › Environments › New environment › `producao`**. Marque **Required reviewers** e inclua você mesmo. É o freio: cada deploy precisa do seu **Approve and run** no painel.

#### 3. Teste

**Actions › Implantar na produção › Run workflow** → informe `v0.1.0` (ou a tag atual). Aprove na pausa do Environment e acompanhe o resumo.

A partir daí, toda release publicada dispara o workflow. Para repetir um deploy, use o botão "Run workflow" do mesmo workflow.

#### Mexer na pasta do deploy

Por padrão o workflow entra em `~/retirada` do `cladmin`. Para mudar, defina a variável `IMPLANTACAO_PASTA` em **Settings › Variables › Actions**.

#### Desligar o automático

Em **Settings › Actions › Runners**, remova o runner — ou pare o serviço (`sudo ./svc.sh stop`). O script `sh deploy/atualizar.sh` manual continua funcionando normalmente.

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

## Limpeza diária do disco

O disco de 10 GB do VPS lota rápido: imagens antigas do Docker, cache de build, backups do `.env` que o workflow de deploy guarda, logs do journal e cache do apt. Quando passa de ~95 %, o `docker compose pull` do próximo deploy trava com `no space left on device`.

Para evitar isso, o `deploy/limpar-disco.sh` roda a cada noite por um timer do systemd (ADR-006: o servidor cuida de si mesmo). O script:
- apaga imagens Docker sem tag em uso e o cache do `buildx` (como `cladmin`);
- mantém só os 3 backups do `.env` mais recentes (configurável);
- limpa o cache do apt;
- comprime logs do journal com mais de 7 dias.

### Instalar (uma vez só, como root)

```bash
cd /home/cladmin/retirada
sudo install -m 0755 deploy/limpar-disco.sh /usr/local/sbin/limpar-disco.sh
sudo install -m 0644 deploy/systemd/retirada-limpeza.service /etc/systemd/system/
sudo install -m 0644 deploy/systemd/retirada-limpeza.timer /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now retirada-limpeza.timer
```

Confere que o timer está armado:

```bash
systemctl list-timers retirada-limpeza.timer
```

Deve aparecer `NEXT` com a próxima execução (padrão: todo dia às 03:30, com jitter de até 5 min).

### Rodar na hora (teste)

```bash
sudo systemctl start retirada-limpeza.service
journalctl -u retirada-limpeza.service -n 40 --no-pager
```

### Ajustar

Se quiser mudar o horário, o usuário ou o número de backups guardados, use overrides do systemd:

```bash
# Horário do timer (ex.: 04:00 em vez de 03:30)
sudo systemctl edit retirada-limpeza.timer
# Variáveis do script (RETIRADA_USUARIO, RETIRADA_PASTA, RETIRADA_MANTER_ENVBAK)
sudo systemctl edit retirada-limpeza.service
```

Para desligar: `sudo systemctl disable --now retirada-limpeza.timer`.

## Backup automático

O `deploy/backup.sh` junta num arquivo só, **cifrado**, os quatro itens que fazem o sistema voltar inteiro ([ADR-013](adr/0013-backup.md)):
- o **banco** (`pg_dump`, com a API rodando);
- o volume **`fotos`**, que é a prova das retiradas (RN14). O serviço de fotos para por alguns segundos durante a cópia;
- o volume **`chaves`**: sem ele, todos saem da sessão e o 2FA por aplicativo precisa ser configurado de novo (as chaves de acesso ficam no banco e continuam valendo);
- o **`.env`**, com as senhas.

O volume `caddy_dados` não entra: ele só evita emitir o certificado de novo.

O arquivo fica em `backups/` no servidor (14 dias) e vai para fora do servidor com o rclone (30 dias). Ele é cifrado com o **age**: quem não tem a chave privada não abre, nem quem levar o disco ou entrar na conta da nuvem.

### 1. Gerar a chave (no seu computador, não no servidor)

```bash
age-keygen -o chave-backup-retirada.txt     # Windows: winget install FiloSottile.age; Linux: sudo apt install -y age
```

- Guarde o arquivo inteiro num **cofre de senhas** (Bitwarden, 1Password, KeePass). Sem ele, **nenhum backup abre**.
- Copie só a linha `# public key: age1...`. Ela vai para o servidor.

### 2. Preparar o servidor

```bash
cd ~/retirada
sudo apt install -y age rclone
sed -i 's/^BACKUP_CHAVE_PUBLICA=.*/BACKUP_CHAVE_PUBLICA=age1.../' .env   # a chave pública do passo 1
```

O `.env` criado antes desta versão não tem as linhas `BACKUP_*`: acrescente-as, copiando do `ferramentas/criar-env.sh`.

**Cópia fora do servidor (recomendado):** sem ela, o backup morre junto com o disco.
1. Rode `rclone config` e crie o destino (por exemplo, `nuvem`). Pode ser Google Drive, OneDrive, Backblaze B2 ou um S3 qualquer.
2. Ponha no `.env`: `BACKUP_DESTINO=nuvem:retirada-backup`.

**Se a Netwise já usa o Veeam:** aponte o Veeam para a pasta `~/retirada/backups`. Ele copia os arquivos já cifrados para o repositório da empresa. Assim, o Veeam faz o papel da cópia fora do servidor, e o `BACKUP_DESTINO` pode ficar vazio. Não use a cópia da máquina inteira (imagem) do Veeam no lugar do `pg_dump`: uma imagem tirada com o banco gravando pode voltar corrompida.

**Aviso de falha (opcional):** `BACKUP_AVISO_URL` recebe o endereço de um monitor de batimento, chamado a cada backup que termina bem. Se o aviso parar de chegar, o backup parou.

### 3. Agendar

Rode uma vez à mão e confira se o arquivo aparece em `backups/`:

```bash
sh deploy/backup.sh
```

Depois, agende para todo dia às 3h30, horário com pouco uso (`crontab -e`, com o usuário que roda o Docker):

```
30 3 * * * cd ~/retirada && sh deploy/backup.sh >> backups/backup.log 2>&1
```

### 4. Testar a restauração (uma vez por mês)

Backup que nunca foi restaurado não é backup. Uma vez por mês, restaure o último numa **outra máquina** (uma VM serve) e confira se o sistema abre com os dados:
1. siga "Primeira vez" até o passo do `.env`, mas **sem** criar o `.env`;
2. copie para lá o arquivo `.tar.age` e a chave privada;
3. rode o `restaurar.sh` (abaixo);
4. confira na própria máquina com `curl -s localhost:8080/health` e abrindo `http://localhost:8080`. O HTTPS não sai no ensaio, porque o domínio aponta para o servidor de verdade;
5. apague a chave privada e desligue essa máquina.

### Restaurar

**Apaga** o banco, as fotos e as chaves do servidor e põe os do backup. O script pede para digitar `RESTAURAR` antes de mexer em qualquer coisa:

```bash
cd ~/retirada
sh deploy/restaurar.sh backups/retirada-AAAAMMDD-HHMMSS.tar.age ~/chave-backup-retirada.txt
rm ~/chave-backup-retirada.txt          # a chave privada não fica no servidor
```

Num servidor novo, sem `.env`, o script usa o `.env` que está dentro do backup, com as mesmas senhas. Para trazer um backup da nuvem: `rclone copy nuvem:retirada-backup/retirada-AAAAMMDD-HHMMSS.tar.age backups/`.

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

## Resgate por PIX (Asaas)

O técnico **terceiro** aperta "Resgatar" e recebe por PIX, pelo Asaas ([ADR-014](adr/0014-resgate-por-pix.md)). O **CLT** recebe na folha e não resgata. Enquanto `PAGAMENTOS_GATEWAY=desligado` (o padrão), o botão aparece, mas avisa que o resgate ainda não está ligado.

### 1. A conta (com o financeiro da Netwise)

1. Abra uma conta no Asaas no **CNPJ da Netwise**, **só para os resgates**. Com a validação ligada (passo 3), o sistema recusa qualquer transferência dessa conta que não seja um resgate.
2. Deixe saldo na conta, com um PIX da conta da empresa. Os resgates saem desse saldo.
3. Em **Integrações › Chaves de API**, gere a chave da API. Ela começa com `$aact_`. Copie direto para o `.env` do servidor e não mande por chat nem por e-mail.

### 2. O `.env` do servidor

```bash
cd ~/retirada
nano .env
```

Confira estas linhas. O `.env` criado antes desta versão não tem nenhuma delas: copie do `ferramentas/criar-env.sh` e gere o token com `openssl rand -hex 24`.

```
PAGAMENTOS_GATEWAY=asaas
RESGATE_MINIMO_CENTAVOS=2000
ASAAS_ENDERECO=https://api.asaas.com
ASAAS_CHAVE_DA_API='$aact_...'
ASAAS_TOKEN_DO_WEBHOOK=<48 letras e números>
```

- **A chave do Asaas fica entre aspas simples.** Sem elas, o Docker lê o `$` do começo como variável e apaga parte da chave.
- **Para testar antes com dinheiro de mentira**, use uma conta do ambiente de testes do Asaas com `ASAAS_ENDERECO=https://api-sandbox.asaas.com`.

Depois de salvar, rode `sh deploy/atualizar.sh`.

### 3. Os avisos (webhooks), no painel do Asaas

O sistema só marca um resgate como **pago** quando o Asaas avisa, com o ID de Fim a Fim. Configure os dois avisos, com o **mesmo token** do `ASAAS_TOKEN_DO_WEBHOOK`:

1. **Eventos.** Em **Integrações › Webhooks › Criar Webhook**, preencha:
   - **URL:** `https://<DOMINIO>/api/webhooks/asaas/eventos`;
   - **Auth Token:** o token do `.env`;
   - **Eventos:** os de transferência, pelo menos `TRANSFER_DONE`, `TRANSFER_FAILED` e `TRANSFER_CANCELLED`.
2. **Validação antes de pagar.** Ligue o mecanismo de validação de saque por webhook, com a URL `https://<DOMINIO>/api/webhooks/asaas/validacao` e o mesmo token. Se o painel não mostrar a opção, peça ao suporte do Asaas.

Sem a validação, o sistema funciona, mas perde a proteção principal: quem roubasse a chave da API poderia mandar PIX por fora.

### 4. Conferir

1. No sistema, o gestor marca um técnico como **Terceiro** (Gestão › Usuários).
2. O técnico cadastra a chave PIX dele e o financeiro aprova (Repasses › Chaves PIX para aprovar).
3. Com o saldo mínimo, o técnico resgata. Em segundos, o resgate aparece como **Pago** na tela dele e na de Repasses, com o ID de Fim a Fim e o comprovante.
4. Se ficar **A caminho** por mais de 30 minutos, confira em **Integrações › Logs de Webhooks** no Asaas se os avisos estão chegando (resposta 200).

**Folha do CLT:** em Repasses, a seção **Folha do mês (CLT)** mostra quanto cada técnico CLT tem no mês. Pague no salário e registre o pagamento na lista de técnicos, como antes.

**Não edite os arquivos do repositório no servidor** (compose, Caddyfile): o deploy leva o código para a tag e recusa se houver mudança local. O que muda por servidor fica no `.env`.

## Chave de acesso (digital ou rosto)

A chave de acesso (ADR-012) fica presa ao `DOMINIO` do `.env`: o compose de produção passa o domínio e a origem `https://<DOMINIO>` para a API. Se o domínio mudar, as chaves cadastradas param de valer, e cada pessoa entra com a senha (e o aplicativo, se tiver) e cadastra de novo em **Minha conta**. Quem perdeu o celular pede ao gestor **Zerar 2FA e chaves**.
