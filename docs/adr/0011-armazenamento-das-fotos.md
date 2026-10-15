# ADR-011 · Armazenamento das fotos da retirada

**Status:** Aceita

## Contexto

A retirada precisa de prova (RN14): foto dos equipamentos recolhidos e, quando a série diverge, foto da etiqueta. Fotos são arquivos grandes, guardados por 5 anos, e mostram o interior da casa do cliente; não podem ficar públicas nem carregar o GPS e o modelo do celular no EXIF. O técnico envia pelo celular, muitas vezes com sinal fraco. O modelo de dados já previa a tabela `evidencia` (só metadados, só acréscimo) e o arquivo num armazenamento S3.

## Decisão

- **Porta e adaptadores (ADR-003):** `IArmazenamentoDeFotos`, com dois adaptadores: pasta no disco (desenvolvimento e testes) e S3 (homologação e produção). Fora do desenvolvimento, sem `Fotos:S3` configurado, a API não sobe.
- **SeaweedFS em homologação**, compatível com S3 (Apache 2.0, imagem `chrislusf/seaweedfs` com versão fixa). Em produção serve qualquer S3 (AWS, provedor nacional, SeaweedFS próprio): muda só `Fotos__S3__*`.
- **Bucket privado:** a foto sai por `GET /api/ordens/{id}/fotos/{fotoId}`, que confere a permissão (técnico só da ordem dele, RNF04). Nada de URL pública ou pré-assinada.
- **Envio em dois passos:**
  1. Cada foto vai sozinha, assim que é tirada (`POST /api/ordens/{id}/fotos`), e fica em `pendentes/`.
  2. A retirada leva só os ids das fotos. O comando confere que cada foto foi enviada pelo mesmo técnico para a mesma ordem, copia o arquivo para `ordens/` e grava a `evidencia` ligada ao evento da retirada, na mesma transação.
- **Sem metadados no arquivo:** o app redesenha a foto num canvas (no máximo 1600 px, JPEG 80%), o que reduz o tamanho e descarta o EXIF. O servidor só aceita JPEG e tira de novo os segmentos de metadados (APP1–APP15 e comentários), porque não confia no cliente. O GPS e a hora que valem ficam nas colunas de `evidencia`.
- **Pendentes esquecidas:** depois que a retirada vale, a pendente é apagada. A que nunca foi usada expira por regra de ciclo de vida do bucket (2 dias); se o armazenamento não aceitar a regra, a API avisa no log.

## Alternativas descartadas

- **MinIO:** era a escolha inicial. A edição comunitária parou de publicar imagens em outubro de 2025 e o repositório foi arquivado em 2026, então não recebe mais correções de segurança.
- **Garage:** leve, mas exige montar o layout do cluster e as chaves por linha de comando antes do primeiro uso.
- **Foto no banco (bytea):** incha o banco e o backup, e o expurgo de 5 anos fica caro.
- **Envio direto ao S3 com URL pré-assinada:** exigiria expor o armazenamento fora da rede do Docker, e o servidor não poderia limpar o EXIF antes de guardar.
- **Fotos no mesmo pedido da retirada (multipart):** um pedido grande, que com sinal fraco falha inteiro e reenvia tudo.

## Consequências

- O volume `fotos` entra no backup, junto com o banco e as chaves.
- Se a transação da retirada falhar depois da cópia, sobra um arquivo em `ordens/` sem `evidencia`: não aparece em lugar nenhum, e a pendente continua lá para a nova tentativa.
- Os testes da API usam o adaptador de pasta. O adaptador S3 roda contra um SeaweedFS de verdade no CI (`RETIRADA_TESTE_S3`).
