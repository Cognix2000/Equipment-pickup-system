# Ambiente de desenvolvimento

Do computador vazio ao sistema rodando com dados fictícios em `http://localhost:8080`.

## 1. Ferramentas

| Ferramenta | Versão | Para quê |
| --- | --- | --- |
| Git | atual | Código |
| Docker Desktop (ou Docker Engine no Linux) | atual, com `docker compose` | Banco, API, app e fotos, iguais à produção |
| .NET SDK | **10** | Rodar e testar o backend fora do Docker |
| Node.js | **22** (a mesma do CI) | Rodar e testar o frontend fora do Docker |
| VS Code | atual | Editor. Extensões: C# Dev Kit, Tailwind CSS IntelliSense, Playwright Test |

Só para **ver o sistema** funcionando, basta Git e Docker. O .NET e o Node servem para desenvolver e rodar os testes.

### Windows 11

No **PowerShell como administrador**:

```powershell
wsl --install                              # Linux (Ubuntu) dentro do Windows; reinicie quando pedir
winget install Git.Git
winget install Docker.DockerDesktop        # no primeiro uso, deixe marcado "Use WSL 2 based engine"
winget install Microsoft.DotNet.SDK.10
winget install OpenJS.NodeJS.22
winget install Microsoft.VisualStudioCode
```

**Recomendado: trabalhe dentro do Ubuntu do WSL.** Clone o repositório em `~/` (no Linux), e não em `C:\`. Lá o Docker fica bem mais rápido e os scripts `.sh` rodam sem adaptação. No VS Code, abra a pasta com a extensão **WSL** (`code .` dentro do Ubuntu).

Dentro do Ubuntu, instale o .NET e o Node do lado Linux:

```bash
sudo apt update && sudo apt install -y git dotnet-sdk-10.0
curl -fsSL https://deb.nodesource.com/setup_22.x | sudo -E bash - && sudo apt install -y nodejs
```

**Fim de linha:** o repositório força `LF` nos `.sh` (`.gitattributes`). Se clonar no Windows mesmo assim, configure antes do clone:

```powershell
git config --global core.autocrlf input
```

Sem isso, os scripts podem falhar com `/bin/sh^M: bad interpreter`.

### Linux (Ubuntu)

Instale o Docker Engine pelo repositório oficial da Docker (com o plugin `docker compose`), depois o `dotnet-sdk-10.0` e o Node 22, como no bloco do Ubuntu acima.

## 2. Subir o sistema

```bash
git clone git@github.com:Cognix2000/Equipment-pickup-system.git retirada && cd retirada
sh ferramentas/criar-env.sh homologacao
```

O script cria o `.env` com senhas aleatórias fortes e permissão só para você. Não existe arquivo de exemplo no repositório, e o `.env` nunca vai para o Git. Depois, suba tudo:

```bash
docker compose up --build
```

Na primeira vez demora alguns minutos, porque monta as imagens. O serviço `migrador` aplica as migrações e a carga fictícia e sai. A API só sobe depois dele.

## 3. Endereços

| Serviço | Endereço | Observação |
| --- | --- | --- |
| App (login) | http://localhost:8080/entrar | Porta muda com `WEB_PORTA` no `.env` |
| App do técnico | http://localhost:8080/tecnico/ordens | Abra no celular ou no modo celular do navegador |
| Painel do financeiro | http://localhost:8080/financeiro/ordens | |
| Repasses | http://localhost:8080/financeiro/repasses | Financeiro e gestor: pagamentos, chaves PIX para aprovar, resgates e folha do mês |
| Resgate do técnico | http://localhost:8080/tecnico/resgate | Técnico terceiro. Na homologação, o PIX é simulado: "cai" na hora |
| Equipe em campo (mapa) | http://localhost:8080/gestao/equipe | Gestor. Na homologação, o mapa é a cidade simulada |
| Gestão de usuários | http://localhost:8080/gestao/usuarios | Gestor |
| Minha conta (chaves de acesso) | http://localhost:8080/conta | Todos |
| Saúde da API | http://localhost:8080/health | `Healthy` quando banco e API estão bem |
| Contrato da API | http://localhost:5080/openapi/v1.json | Só com a API rodando fora do Docker (item 4). A versão aprovada fica em `backend/tests/Retirada.Arquitetura.Testes/Contrato/openapi.v1.json` |
| PostgreSQL | `127.0.0.1:5432`, banco `retirada` | Usuário `retirada_dono`, senha `BANCO_SENHA_DONO` do `.env` |
| Grafana (logs, métricas, rastros) | http://localhost:3000 | Só com `--profile observabilidade`; entra com admin/admin |
| Fotos (SeaweedFS) | sem porta na máquina | Só dentro do Docker; quem entrega a foto é a API |

### Usuários de homologação

A senha de todos é `Demo@Retirada2026`, e só vale para os e-mails `@exemplo.invalid`.

| E-mail | Papel | Segundo fator |
| --- | --- | --- |
| `tecnico.demo@exemplo.invalid` | Técnico (CLT: recebe na folha) | não |
| `terceiro.demo@exemplo.invalid` | Técnico (terceiro: resgata por PIX simulado, chave já aprovada) | não |
| `estoque.demo@exemplo.invalid` | Estoque | não |
| `financeiro.demo@exemplo.invalid` | Financeiro | sim, no primeiro login |
| `gestor.demo@exemplo.invalid` | Gestor | sim, no primeiro login |

No primeiro login de gestor e financeiro, escolha **"Cadastrar a digital ou o rosto"** (chave de acesso) ou um aplicativo autenticador. A chave de acesso só funciona em `localhost` ou com HTTPS.

## 4. Desenvolver com recarga automática

O banco fica no Docker, e a API e o app rodam na sua máquina. Toda mudança aparece na hora. O passo a passo está no [README, "Desenvolvimento com recarga automática"](../README.md#desenvolvimento-com-recarga-automática):

- API em http://localhost:5080;
- app em http://localhost:5173 (repassa `/api` para a 5080).

## 5. Problemas comuns

| Sintoma | Causa | Solução |
| --- | --- | --- |
| `defina BANCO_SENHA_DONO no .env` | Falta o `.env` | `sh ferramentas/criar-env.sh homologacao` |
| `port is already allocated` na 8080 | Outra coisa usa a porta | `WEB_PORTA=8081` no `.env` |
| `port is already allocated` na 5432 | Um PostgreSQL instalado na máquina | Pare esse serviço ou mude a porta do `banco` no compose |
| A API não sobe | O migrador falhou | `docker compose logs migrador` |
| Login com a senha de demonstração falha | Banco criado antes, com outra senha | Zere tudo: `docker compose down -v && docker compose up --build` (**apaga o banco, as fotos e as chaves**) |
| `/bin/sh^M: bad interpreter` | Script com fim de linha do Windows | `git config --global core.autocrlf input`, depois clone de novo (ou rode no WSL) |
| Botão da digital não aparece | Endereço sem HTTPS (ex.: IP da rede) | Use `localhost` ou o aplicativo autenticador |
| O Docker está muito lento no Windows | Repositório em `C:\` | Clone dentro do Ubuntu do WSL (`~/`) |

Mais comandos estão em [comandos.md](comandos.md).
