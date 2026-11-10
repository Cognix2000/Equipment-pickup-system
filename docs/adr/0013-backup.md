# ADR-013 · Backup diário cifrado, com cópia fora do servidor

**Status:** Aceita.

## Contexto

A produção roda num servidor só, com Docker. Quatro coisas fazem o sistema voltar inteiro:
- o banco;
- as fotos da retirada, que são prova e ficam guardadas por 5 anos (RN14);
- as chaves do Data Protection, que assinam a sessão e cifram o 2FA (ADR-010);
- o `.env`, com as senhas.

Até aqui, o backup era manual e dependia de alguém lembrar. Se o disco morresse, ia tudo junto.

O backup leva o `.env` e os dados de clientes. Por isso ele não pode ficar legível na nuvem, nem para quem levar o disco do servidor.

## Decisão

- **Um script, `deploy/backup.sh`, agendado no cron** todo dia de madrugada. Ele faz:
  - o banco por `pg_dump` (formato custom), consistente com a API rodando, e confere se o dump abre (`pg_restore --list`);
  - as fotos por `tar` do volume, com o SeaweedFS parado por alguns segundos para a cópia sair inteira;
  - as chaves por `tar` do volume;
  - o `.env`, mais um `SHA256SUMS` de tudo.
- **Tudo num arquivo só, cifrado com o [age](https://age-encryption.org).** O servidor tem só a chave **pública**. A privada fica com o Pedro, num cofre de senhas, e só vai ao servidor na hora de restaurar.
- **Cópia fora do servidor com o rclone** (qualquer nuvem ou S3), configurada no `.env` (`BACKUP_DESTINO`). Ficam 14 dias no servidor e 30 no destino remoto.
- **`deploy/restaurar.sh`** confere a soma, pede para digitar `RESTAURAR`, recria o banco a partir do dump e repõe as fotos e as chaves. Num servidor novo, usa o `.env` do backup.
- **Teste de restauração todo mês**, numa outra máquina (docs/implantacao.md).
- **Aviso opcional de batimento** (`BACKUP_AVISO_URL`): se o aviso parar de chegar, o backup parou.

## Alternativas descartadas

- **Só a imagem da máquina inteira (Veeam, snapshot da VPS):** copia o disco com o banco gravando, e o banco pode voltar corrompido. Também não separa o que restaurar. Se a Netwise já tem o Veeam, ele **complementa**: aponta para a pasta `backups/`, que já está cifrada, e faz o papel da cópia fora do servidor.
- **Backup contínuo do banco (WAL, pgBackRest):** dá para voltar a qualquer minuto, mas exige mais uma peça para manter. Com o volume de hoje, perder no máximo um dia é aceitável. Revemos quando a operação crescer.
- **Copiar as fotos pela API S3 (rclone sync do bucket):** não para o serviço, mas leva os metadados do SeaweedFS separados do resto e deixa a restauração mais difícil. O `tar` do volume com o serviço parado por segundos é mais simples.
- **Cifrar com senha (`gpg --symmetric`):** a senha teria de ficar no servidor para o cron rodar, e quem invadisse o servidor abriria os backups. Com o age, o servidor só cifra.

## Consequências

- O servidor precisa de `age` e `rclone` (`apt install`), e o `.env` ganha as linhas `BACKUP_*`.
- **Perder a chave privada é perder todos os backups.** Ela precisa estar no cofre de senhas, e de preferência com mais de uma pessoa da empresa.
- Uma foto enviada no instante da cópia falha e fica com "Tentar de novo" no celular. Por isso o backup roda às 3h30.
- 30 dias de backup completo na nuvem ocupam 30 vezes o tamanho das fotos. Quando ficar caro, a troca é por cópia incremental das fotos (rclone sync) mais o dump diário.
