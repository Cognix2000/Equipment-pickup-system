# ADR-008 · Observabilidade com OpenTelemetry e Grafana

**Status:** Aceita

## Contexto

Saber que algo quebrou antes do usuário avisar, sem tirar dados de cliente do data center. A Netwise já opera Grafana no monitoramento da rede.

## Decisão

- Instrumentação com OpenTelemetry na API (logs, métricas e rastros, inclusive das consultas ao banco) e Grafana Faro no PWA.
- Destino: Grafana com Loki, Tempo e Prometheus no data center, recebendo pelo Grafana Alloy.
- Desenvolvimento e homologação: `grafana/otel-lgtm`, perfil `observabilidade` do compose.
- Alertas iniciais: API fora do ar por 2 min, 5xx acima de 1% em 5 min, p95 acima da meta, importação com falha, backup do dia sem rodar.

## Alternativas descartadas

- Datadog ou New Relic: pagos por host e volume; dados fora da Netwise.
- Elastic (Elasticsearch, Kibana e APM Server): gratuito no próprio servidor e com busca em logs melhor, mas medido com ~2,7 GB de RAM com heap mínimo (4 a 5 GB em uso real), contra ~0,5 GB do Grafana na mesma carga. A integração por OTLP funcionou igual, então trocar no futuro não muda código.
- Sentry: bom para erros, mas sem métricas e rastros; auto-hospedado é pesado.
- Seq: só logs.

## Consequências

- Servidor dimensionado em 4 vCPU, 8 GB de RAM e 100 GB de SSD (medição com carga fictícia de 35 usuários simultâneos).

- Sem `OTEL_EXPORTER_OTLP_ENDPOINT`, a API não exporta nada.
- Log e rastro levam só identificadores; nome, telefone e endereço nunca entram (LGPD).
- Trocar de ferramenta muda só o destino do OTLP.
