# ADR 0002: Outbox MongoDB do MassTransit e Unit of Work

## Status
Aceito (verificado em Mongo 7 e RabbitMQ 4; Atlas M0 ainda nao verificado)

## Contexto
O estado da ordem e a mensagem para a saga (`DiagnosticoIniciado`, `DiagnosticoConcluido`,
`ExecucaoFinalizada`, `ExecucaoInviavel`) precisam ser atomicos: nunca ordem alterada sem evento, nem evento
sem ordem alterada. As mensagens chegam at-least-once (a mesma pode ser entregue varias vezes e ate em
paralelo). O MassTransit 8.5.12 (a 9.x e comercial) oferece o outbox/inbox transacional para MongoDB
(`MassTransit.MongoDb`). O spike descartavel que antecedeu este servico mediu o comportamento e deixou
condicoes sem as quais a combinacao falha.

## Decisao
- **Publicar antes do commit**: todo handler publica por `IIntegrationEventPublisher` ANTES de
  `IUnitOfWork.SaveChangesAsync`; a implementacao (`MassTransitIntegrationEventPublisher`) so faz `Publish` e
  copia o `MessageId` do contrato para o envelope (acessor compilado uma vez por tipo), que e o que o inbox
  do consumidor usa para deduplicar.
- **Configuracao do outbox**: `AddMongoDbOutbox` com `UseBusOutbox` (publicacao fora de consumer, ou seja,
  no caminho HTTP) e `UseMongoDbOutbox` no endpoint `Filas.Execucao`; janela de deduplicacao de 30 minutos
  (a idempotencia natural do dominio e a segunda defesa); `MassTransitHostOptions.WaitUntilStarted = true`.
- **Retry por fora do outbox**: `UseMessageRetry` (intervalos exponenciais, `Ignore<ValidationException>()` e
  `Ignore<DomainException>()`) e registrado antes de `UseMongoDbOutbox` e fica POR FORA do filtro do outbox, de modo
  que uma falha transitoria do proprio outbox (registro no inbox, abertura ou commit da transacao) e retentada em
  vez de ir para a fila de erro (no spike, 18 a 20 de 40 duplicatas concorrentes caiam em `_error` sem o retry).
  Medido no 8.5.12: o `UseMongoDbOutbox` entra como filtro do consumer, entao a ordem das duas chamadas no endpoint
  e indiferente (a pilha de chamadas mostra o `RetryFilter` por fora nos dois casos); o que quebra e mover o retry
  para dentro do consumer, o que um teste de infra cobre injetando a falha no filtro do outbox. Excecao de negocio
  nao melhora com nova tentativa e vai direto para `_error`; as demais (inclusive `KeyNotFoundException` e
  `ConcorrenciaException`) sao retentadas.
- **Prontidao e broker fora**: com o outbox o servico continua aceitando escrita com o broker caido (a mensagem
  espera no banco), entao o `/health/ready` NAO pode tirar o pod de rotacao por causa do RabbitMQ: o MongoDB
  devolve `Unhealthy` (503) e o broker devolve `Degraded` (200) por uma conexao AMQP real de limite curto, com o
  check do bus do MassTransit rebaixado do mesmo jeito. Readiness em cascata viraria indisponibilidade total. O
  alerta do broker fica por metrica (`outbox_mensagens_pendentes`).
- **Guarda de configuracao**: o transporte em memoria e a ausencia de outbox so valem em Development e Testing
  (`ValidateOnStart`); valor fora do enum falha em qualquer ambiente e o codigo que escolhe transporte e outbox
  lanca em valor desconhecido, em vez de cair para memoria sem outbox.
- **Pre-criar colecoes e indice do inbox/outbox** (`inbox.states`, `outbox.messages`, `outbox.states` e o
  indice unico `{MessageId, ConsumerId}`) num hosted service registrado antes do bus: a criacao em background
  do `InboxCleanupService` colide com a primeira mensagem (DDL dentro de transacao).
- **Unit of Work com commit explicito**: o MongoDB nao tem "SaveChanges", e publicar sem commit perde o evento
  em silencio. No caminho HTTP, `TransacaoBehavior` (pipeline do MediatR, depois do `ValidationBehavior`)
  abre a transacao do `MongoDbContext` do MassTransit, e `MongoUnitOfWork.SaveChangesAsync` a confirma. So
  os commands cujo handler publica entram (`ReivindicarProximoDiagnostico`, `ConcluirDiagnostico`,
  `FinalizarReparo`, `InviabilizarExecucao`; um teste confere a lista contra os handlers que dependem do
  publisher). Os demais gravam um unico documento com checagem de `version`, que ja e atomico.
- **Retry de `TransientTransactionError` no HTTP**: o driver nao refaz o corpo da transacao sozinho; o
  behavior reexecuta o handler INTEIRO (ate 8 vezes, com backoff e jitter) e, esgotadas as tentativas, lanca
  `ConcorrenciaException` (409 na API). Dentro de um consumer a transacao e a do filtro do outbox, quem a
  confirma e quem retenta e o `UseMessageRetry`: o UoW nao confirma de novo e o behavior apenas repassa.
- **Claim dentro e fora de transacao**: a leitura da fila nunca usa a sessao (enxerga o estado commitado, e
  uma `ConcorrenciaException` devolve a proxima ordem, nao a mesma); a escrita do claim que publica
  `DiagnosticoIniciado` usa a transacao do outbox. O claim de reparo nao publica e roda sem transacao.
- **Repositorios usam a sessao corrente quando existir** (`IMongoSessionAccessor`); `AtualizarAsync` grava
  na hora e lanca `ConcorrenciaException` por filtro de `version`, nunca o commit.
- **Metricas de negocio so depois do commit**: um buffer por escopo e descarregado pelo UoW apos a
  confirmacao e descartado quando a transacao e abortada e reexecutada. Dentro de consumer a descarga
  acontece antes do commit do filtro do outbox; se esse commit falhar, a mensagem e reprocessada e a metrica
  conta de novo (metrica operacional, nao contabil).

## Consequencias
### Positivas
- Atomicidade provada com Mongo e RabbitMQ reais: falha depois de gravar e publicar deixa 0 documentos
  alterados e 0 mensagens no outbox; a mesma mensagem entregue varias vezes (inclusive em paralelo) executa o
  handler uma vez; o registro do inbox de uma tentativa que falhou nao impede o reenvio.
- A regra "publicar antes do commit" fica na Application (interface) e a tecnologia na Infrastructure.
- Pontos de troca (`Mensageria:Transporte` e `Mensageria:Outbox`) permitem testar consumers, politica de
  retry e API sem Docker.

### Negativas
- Retry reexecuta o handler inteiro: os handlers precisam ser idempotentes e sem efeito colateral antes do
  commit (e sao, exceto o proprio outbox, que e transacional).
- O outbox custa ~37 operacoes de banco por mensagem e ha polling de entrega (`QueryDelay`); no M0 isso
  consome parte das ~100 operacoes por segundo.
- Com `WaitUntilStarted = true` e o broker inalcancavel no boot, a API nao escuta e nao ha limite de tempo (medido,
  sem crash): uma `startupProbe` finita leva o pod a CrashLoopBackOff ate o broker voltar, e nenhum pod NOVO sobe
  durante uma queda do broker. Trade-off aceito para a F7; os pods ja de pe seguem atendendo por causa do outbox.
- `UnknownTransactionCommitResult` (commit com resultado desconhecido) nao e tratado separadamente: a falha
  sobe e e retentada como qualquer outra, com a idempotencia de mensagem/negocio como protecao.
- Dois commits de transacao com semantica diferente (HTTP confirma, consumer delega ao MassTransit) precisam
  ser lembrados por quem mexer no UoW; o teste de infra cobre os dois caminhos.

## Alternativas consideradas
- **Outbox embutido no documento + relay proprio**: implementado no spike, funciona ate em Mongo standalone e
  custa ~4,2 operacoes por mensagem, mas sem lease nem inbox o relay entrega duplicado (3 a 4x com 4 relays);
  fica como plano B se o Atlas M0 recusar transacoes.
- **Publicar depois do commit**: perde a mensagem se o processo cair entre os dois passos.
- **Outbox em memoria do MassTransit**: nao sobrevive a reinicio nem e atomico com o banco.
- **Change streams do Mongo como fonte dos eventos**: acopla o formato do documento ao contrato de mensagem
  e exige gerenciar resumo de stream e deduplicacao.
