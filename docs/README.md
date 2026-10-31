# Documentação · comece por aqui

Mapa de tudo o que está escrito sobre o projeto, para uma pessoa ou um agente de IA entender o sistema **antes** de mexer nele.

## Ordem de leitura

| # | Documento | Para quê | Quando ler |
| --- | --- | --- | --- |
| 1 | [AGENTS.md](../AGENTS.md) | Regras para qualquer agente de IA | Sempre, antes de agir |
| 2 | [CLAUDE.md](../CLAUDE.md) | Contexto completo: regras inegociáveis, padrões, armadilhas e requisitos | Sempre, antes de agir |
| 3 | [ambiente.md](ambiente.md) | Instalar as ferramentas (Windows 11 ou Linux) e subir o sistema na sua máquina | Primeiro dia |
| 4 | [comandos.md](comandos.md) | Comandos do dia a dia: Docker, .NET, npm, banco, Git e produção | Sempre que precisar |
| 5 | [arquitetura.md](arquitetura.md) | Como as partes se ligam, o caminho de um comando e o ciclo da ordem | Antes de mexer no código |
| 6 | [modelo-de-dados.md](modelo-de-dados.md) | Tabelas, relações e regras do banco | Antes de mexer no banco ou numa consulta |
| 7 | [adr/](adr/README.md) | Por que cada decisão técnica foi tomada | Antes de propor mudar uma decisão |
| 8 | [CONTRIBUTING.md](../CONTRIBUTING.md) | Branch, commit, PR e definição de pronto | Antes do primeiro PR |
| 9 | [implantacao.md](implantacao.md) | Servidor de produção, HTTPS, atualização e backup | Ao mexer na produção |
| 10 | [roteiro-de-demonstracao.md](roteiro-de-demonstracao.md) | Apresentar o sistema, passo a passo | Antes de uma demonstração |
| — | [README.md](../README.md) | Visão geral, como rodar e o que cada parte faz | Para conhecer o produto |

## Onde está cada coisa

| Procurando | Onde |
| --- | --- |
| Uma regra de negócio (RN03, RN09…) | O código e o resumo estão no [CLAUDE.md](../CLAUDE.md). A explicação detalhada fica no documento do projeto, **fora do repositório**: peça acesso ao Pedro |
| Como a API responde | `backend/tests/Retirada.Arquitetura.Testes/Contrato/openapi.v1.json` (o contrato versionado) |
| Uma regra no código | `backend/src/Modulos/*/Dominio/` (a regra) e `Casos/` (um caso de uso por arquivo) |
| O esquema do banco | `backend/src/Retirada.Banco/Scripts/` (uma migração SQL por arquivo, em ordem) |
| Uma tela | `frontend/src/paginas/` (técnico, painel, estoque, acesso e conta) |
| Os dados fictícios | `db/dados-ficticios/` e `ferramentas/dados-ficticios/` |
| A esteira do CI | `.github/workflows/` |

## Manter atualizado

A documentação muda no **mesmo PR** do código:

| Se o PR... | Atualize |
| --- | --- |
| Cria uma tabela | [modelo-de-dados.md](modelo-de-dados.md) |
| Cria um módulo ou muda o caminho de um comando | [arquitetura.md](arquitetura.md) |
| Muda um comando ou uma porta | [comandos.md](comandos.md) ou [ambiente.md](ambiente.md) |
| Toma uma decisão técnica | Uma ADR nova |
