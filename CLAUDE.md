# OficinaMecanica.Execucao - Fila de Execucao, Diagnostico e Reparo

## Visao Geral

Microsservico (.NET 8) do ecossistema OficinaMecanica responsavel pela
producao na oficina: fila de ordens, diagnostico, reparo, apontamentos do
mecanico e laudo. Fala com o `OficinaMecanica` (OS Service, que hospeda a
saga) so por mensagens (RabbitMQ + MassTransit 8.5.12): recebe `SolicitarDiagnostico`,
`IniciarExecucao` e `CancelarProducao` no endpoint `Filas.Execucao`
(`execucao-comandos`); publica `DiagnosticoIniciado`, `DiagnosticoConcluido`,
`ExecucaoFinalizada` e `ExecucaoInviavel` pelo outbox transacional do MongoDB.
O mecanico opera pela API HTTP (JWT do Seguranca). Nenhuma dependencia de
compilacao com outro repositorio e nenhum acesso ao banco de outro servico.

Estado atual: Domain, Application, Infrastructure (MongoDB + MassTransit/RabbitMQ),
API, Dockerfile, docker-compose, testes (incluindo Testcontainers) e o CI/CD
(`.github/`), os manifests (`k8s/execucao/`), a protecao de branch versionada e os
scripts de `scripts/` prontos. O `README.md` e o `docs/cobertura.md` estao prontos; falta o teste real no Atlas M0
(precisa de credenciais do usuario). O repositorio ainda nao existe no GitHub: o CI/CD foi
validado so localmente (`actionlint`, `kubeconform`, `docker build`, execucao da imagem
contra MongoDB e RabbitMQ reais, execucao dos scripts e do `ci-local.sh`); a primeira
execucao real do workflow acontece na publicacao.

## Estrutura do Projeto

```
src/
  OficinaMecanica.Contracts/                # copia do subconjunto de mensagens usado aqui (ver "Contratos")
  OficinaMecanica.Execucao.Domain/          # OrdemDeProducao (aggregate), CancelamentoPrevio, value objects, enums,
                                             # repositorios (IOrdemDeProducaoRepository, ICancelamentoPrevioRepository) e IUnitOfWork
  OficinaMecanica.Execucao.Application/     # Commands/Queries (MediatR), Validators (FluentValidation), DTOs,
                                             # ValidationBehavior, IIntegrationEventPublisher
  OficinaMecanica.Execucao.Infrastructure/  # MongoDB (mapeamento BsonClassMap, repositorios, UoW, indices), MassTransit
                                             # (consumers finos, publisher, outbox), JWT/JWKS, metricas
    Persistence/   MongoMappings, OrdemDeProducaoRepository, CancelamentoPrevioRepository, MongoUnitOfWork,
                   TransacaoMongo (+ MassTransit), TransacaoBehavior + PoliticaDeTransacao, MongoInitializer, MongoHealthCheck
    Health/        HealthCheckComCache (cache do resultado da prontidao) + OpcoesDaProntidao + AddCheckComCache
    Messaging/     MensageriaOptions, MassTransitIntegrationEventPublisher, Consumers/ (SolicitarDiagnostico, IniciarExecucao, CancelarProducao)
    Observability/ MetricasExecucao (Meter), MetricasPendentes, MetricasDaFila (+ BackgroundService), MongoIdadeDaFilaReader
    Authentication/ JsonWebKeySetRetriever, ResolvedorDeChavesJwks, AutenticacaoJwtExtensions
    DependencyInjection.cs   # AddInfrastructure = AddPersistenciaMongo + AddMensageria + AddMetricasDaFila + AddAutenticacaoJwt
  OficinaMecanica.Execucao.API/             # Program.cs, Controllers (so traduzem HTTP <-> MediatR), ExceptionHandlingMiddleware,
                                             # Swagger/OTel/Serilog em Extensions/, Health/ProntidaoResponseWriter (corpo de /health/ready)
tests/
  OficinaMecanica.Contracts.Tests/          # goldens JSON dos contratos copiados
  OficinaMecanica.Execucao.Domain.Tests/    # sem mocks
  OficinaMecanica.Execucao.Application.Tests/  # Moq, um arquivo por handler/validator
  OficinaMecanica.Execucao.Integration.Tests/  # sem Docker (mapeamento BSON, consumers, publisher, behavior, API via
                                             # WebApplicationFactory com fakes) + [Trait("Category","Infra")] com Testcontainers
  OficinaMecanica.Execucao.Tests.Common/    # builders Bogus (OrdemDeProducaoBuilder etc.) e RelogioFixo
docs/adr/                                    # 0001 MongoDB Atlas, 0002 outbox MongoDB do MassTransit + Unit of Work, 0003 CI/CD
docs/openapi.json                            # contrato OpenAPI versionado (o CI falha se estiver desatualizado)
.github/                                     # workflows/ci.yml, rulesets/protect-main-release.json, dependabot.yml, PULL_REQUEST_TEMPLATE.md
k8s/execucao/                                # deployment, service, ingress, hpa, secret-provider-class
scripts/                                     # cobertura.sh, verificar-cobertura.sh, verificar-masstransit.sh, verificar-workflow.sh,
                                             # exportar-swagger.sh, validar-k8s.sh, ci-local.sh, aplicar-ruleset.sh e testar-scripts.sh
                                             # (os testes de todos eles: casos de falha + mutantes)
Dockerfile, docker-compose.yml, .env.example, .dockerignore, coverage.runsettings
dotnet-tools.json                            # ReportGenerator (merge de cobertura) e Swashbuckle CLI (OpenAPI)
```

Mesmos principios do `OficinaMecanica`: Clean Architecture (Domain ->
Application -> Infrastructure -> API), DDD, CQRS via MediatR,
`ValidationBehavior`, Repository pattern (interface no Domain).

## Convencoes

- **Idioma do dominio**: entidades, propriedades, enums, mensagens de erro e
  de validacao em portugues (com acento nas strings); namespaces, camadas e
  nomes de pattern em ingles
- Comentarios so quando explicam um "porque" nao obvio, em primeira pessoa
  ("optei por"), nunca em segunda pessoa e sem narrar incidente
- Testes que aguardam uma tarefa que o codigo sob teste pode deixar de liberar (`TaskCompletionSource`) usam `.ComTetoAsync("...")` (`Support/EsperaLimitada`, teto de 6 s): sem a protecao o teste FALHA com mensagem clara em vez de travar o run
- Testes: xUnit + FluentAssertions (sem `Assert.*` puro) + Moq (no Application.Tests e, no
  Integration.Tests, onde um fake em memoria nao serve: consumers que mapeiam contrato -> command, health
  checks, middleware e metricas HTTP) + Bogus nos builders do `Tests.Common`; Domain.Tests nao usa mock
- O "agora" nunca vem de `DateTime` dentro do dominio: entra por parametro
  (`DateTimeOffset`) e os handlers leem de `TimeProvider`

## Dominio

`OrdemDeProducao` tem como `Id` o proprio `OrdemDeServicoId` (idempotencia do
enfileiramento). Fluxo:

```
Fila -> EmDiagnostico -> DiagnosticoConcluido -> AguardandoExecucao -> EmExecucao -> Finalizada
```

- `Enfileirar` guarda tambem as `Observacoes` (queixa do cliente, texto livre) e
  aceita lista de servicos vazia: o diagnostico ainda define o que fazer, e uma
  excecao de negocio num consumer viraria mensagem envenenada. A regra "ao menos
  um servico" vale so em `LiberarParaExecucao`, que recebe o snapshot aprovado
- `Inviabilizar(mecanicoId, motivo)` vale a partir de `AguardandoExecucao` e
  `EmExecucao` (-> `Inviavel`). **Decisao**: em `EmExecucao` so o mecanico
  responsavel inviabiliza; em `AguardandoExecucao` ninguem assumiu o reparo, entao
  qualquer mecanico/supervisor identificado pode (o dominio so exige um
  `mecanicoId` nao vazio). Quem inviabilizou fica sempre em `InviabilizadaPor`.
  **Papel na API**: o Seguranca so emite o perfil `Admin` hoje, entao nao ha papel
  distinto para "mecanico" ou "supervisor": todo endpoint e `[Authorize]` simples e o
  mecanico e o `sub` do token. Restringir por papel exige evoluir o Seguranca primeiro
- `Cancelar` vale a partir de `Fila`, `EmDiagnostico`, `DiagnosticoConcluido` e
  `AguardandoExecucao`; nao de `EmExecucao`, `Finalizada` ou `Inviavel`
- `Cancelar` devolve `false` na reentrega (ja `Cancelada`). `LiberarParaExecucao`
  devolve `false` (no-op) quando a ordem ja passou da liberacao
  (`AguardandoExecucao`, `EmExecucao`, `Finalizada`, `Inviavel`); `Cancelada` ou
  antes do diagnostico concluido continuam `DomainException`. Na primeira
  liberacao substitui servicos e pecas pelo snapshot aprovado de `IniciarExecucao`
- Apontamento, conclusao do diagnostico e finalizacao do reparo so valem para o
  mecanico responsavel (`MecanicoId` e um id opaco, comparado sem normalizar
  caixa); quem inicia o reparo vira o responsavel
- Textos livres (laudo, resumo, motivos, observacoes, apontamento) sao gravados e
  publicados ja com `Trim()`
- `CancelamentoPrevio` (tombstone, `Id` = `OrdemDeServicoId`): `CancelarProducao` para
  ordem inexistente registra um tombstone idempotente, e o enfileiramento que chegar
  depois e no-op (nada e publicado, nao ha evento de resposta para isso). Mesma
  regra "cancelar antes de criar" do ADR 0003 do `OficinaMecanica`
- Violacao de regra -> `DomainException`; conflito de `Version` ->
  `ConcorrenciaException` (lancada pela implementacao do repositorio, nao herda
  de `DomainException`)
- `LiberadaEm` (momento em que a ordem ficou `AguardandoExecucao`) esta sempre
  preenchida a partir da liberacao: e ela, e nao `EnfileiradaEm`, que ordena a
  fila de reparo
- `IncrementVersion` e publico porque quem incrementa e o repositorio (outro
  assembly). `_etapas` e `_apontamentos` deixaram de ser `readonly` (unica mudanca no
  Domain por causa da persistencia): a reidratacao por `BsonClassMap` preenche o campo privado e o
  driver nao grava em campo readonly

## Application

- Um Command/Query + Handler + Validator por caso de uso (pasta por caso de uso).
  Ordem inexistente vira `KeyNotFoundException`, exceto em `CancelarProducao`
  (compensacao da saga: registra o tombstone, ver "Dominio")
- `ReivindicarProximoDiagnostico`/`ReivindicarProximoReparo`: leem a proxima da
  fila (FIFO por `EnfileiradaEm` no diagnostico e por `LiberadaEm` no reparo) e
  gravam com checagem de `Version`; em
  `ConcorrenciaException` tentam a proxima (ate 5 vezes). Fila vazia e um
  resultado (`ResultadoDaReivindicacaoDto.FilaVazia`), nao excecao
- **Regra do outbox**: todo handler publica via `IIntegrationEventPublisher`
  ANTES de `IUnitOfWork.SaveChangesAsync` (a implementacao real grava no outbox
  na mesma transacao). Nunca publicar depois do commit
- `AddApplication()` registra MediatR, `ValidationBehavior`, validators e
  `TimeProvider.System` (`TryAdd`)

## Infrastructure

### Persistencia (MongoDB)

- Colecao `ordens_producao`; `_id` = `OrdemDeServicoId`; campos camelCase; enums como texto; datas como
  data BSON UTC (precisao de ms); `version` long; `GuidRepresentation.Standard` registrado em
  `MongoMappings.Registrar()` ANTES de criar o `MongoClient`. Mapeamento por `BsonClassMap` (o Domain nao
  referencia o driver); agregados reidratados sem passar pelo construtor
- **Tombstone na MESMA colecao e MESMA chave**, discriminador `_t` (`ordem` | `cancelamentoPrevio`). Toda
  leitura de ordem, fila, listagem e contagem filtra `_t = ordem`. Colisao de `_id` fora de transacao:
  `AdicionarAsync` devolve `false` (ordem x ordem, ou ordem batendo em tombstone); tombstone batendo em
  ordem lanca `ConcorrenciaException`; dentro de transacao o E11000 sobe e o retry reexecuta o handler
- `AtualizarAsync` grava na hora com filtro `{_id, _t, version}` e lanca `ConcorrenciaException` se nao casar
  (a `Version` em memoria so avanca depois da escrita aceita). `ObterProximaDaFilaAsync` le SEMPRE sem a
  sessao da transacao (releitura enxerga o estado commitado); `ObterPorIdAsync`/`ObterAsync` usam a
  sessao corrente quando existir
- Indices criados no startup por `MongoInitializer` (hosted service registrado ANTES do bus; idempotente):
  `ix_status_enfileiradaEm`, `ix_status_liberadaEm`, `ix_mecanicoId_status`, `ux_numero` (unico parcial
  so de ordens) + pre-criacao de `inbox.states`, `outbox.messages`, `outbox.states` e do indice unico
  `{MessageId, ConsumerId}` do inbox
- `MongoUnitOfWork.SaveChangesAsync` confirma a transacao aberta pelo HTTP e publica as metricas pendentes;
  dentro de consumer a transacao e a do outbox do MassTransit, que a confirma (o UoW nao confirma de novo).
  `TransacaoBehavior` (so para os 4 commands que publicam; ver `PoliticaDeTransacao`) abre a transacao e
  reexecuta o handler INTEIRO em `TransientTransactionError` (ate 8x, backoff com jitter; esgotado vira
  `ConcorrenciaException` -> 409). O claim de diagnostico (publica `DiagnosticoIniciado`) esta na MESMA
  transacao do outbox; o claim de reparo (nao publica) roda sem transacao

### Mensageria (MassTransit 8.5.12 + RabbitMQ)

- `AddMensageria(configuration, configurarBus?)`: bus RabbitMQ (`RabbitMq:*`), `WaitUntilStarted = true`,
  `AddMongoDbOutbox` + `UseBusOutbox`, endpoint `Filas.Execucao` com 3 consumers finos
  (`SolicitarDiagnostico`, `IniciarExecucao`, `CancelarProducao` -> command MediatR via `DaMensagem`)
- Retry no endpoint (intervalos exponenciais; `Ignore<ValidationException>`, `Ignore<DomainException>`),
  registrado ANTES de `UseMongoDbOutbox` e POR FORA do filtro do outbox: uma falha transitoria do proprio outbox
  (inbox, abertura ou commit da transacao) e retentada, nao vai para `_error`. **Medido no 8.5.12**: o
  `UseMongoDbOutbox` entra como filtro do consumer, entao a ORDEM das duas chamadas e indiferente (a pilha de
  chamadas mostra o `RetryFilter` por fora nos dois casos; inverter a ordem e um mutante equivalente). O que quebra
  e mover o retry para dentro do consumer: `RetryAntesDoOutboxTests` injeta a falha no filtro do outbox (cliente
  Mongo com proxy) e esse mutante falha. `KeyNotFoundException` e `ConcorrenciaException` sao retentadas;
  esgotadas, a mensagem vai para `execucao-comandos_error` (e o `Fault<T>` e publicado)
- Pontos de troca para teste: `Mensageria:Transporte` (`RabbitMq` | `EmMemoria`) e `Mensageria:Outbox`
  (`MongoDb` | `Nenhum`). **Guarda de producao** (`ValidadorDasOpcoesDaMensageria`, `ValidateOnStart`, registrada
  antes de qualquer conexao): `EmMemoria`/`Nenhum` so em `Development` e `Testing`; `RabbitMq:Host/Username/Password`
  obrigatorios com o transporte RabbitMq. **Fail-closed**: numero fora do enum (`Transporte=9`) falha em QUALQUER
  ambiente (o binder aceita qualquer numero) e `MensageriaOptions.UsaRabbitMq`/`UsaOutboxDoMongo` sao `switch` que
  lancam em valor desconhecido, nunca um "senao, em memoria"; texto invalido falha no binder. Testes de partida
  variam UMA chave por caso, em `Production` e `Development`
- **Prontidao (`/health/ready`)**: o MongoDB devolve `Unhealthy` (503, tira o pod de rotacao); o broker devolve
  `Degraded` (HTTP 200) por `RabbitMqHealthCheck` (conexao AMQP real, limite de 3 s, inclusive com o broker congelado: o `WaitAsync` garante o limite porque o cliente
  AMQP leva ate ~8 s para desistir) e o check `masstransit-bus` do
  MassTransit tem a falha rebaixada para `Degraded`. Decisao de desenho (vale para OS, Billing e Execucao): com o
  outbox transacional o servico continua aceitando escrita com o broker fora (a mensagem espera no banco), entao
  broker caido nao pode derrubar a rotacao (readiness em cascata = indisponibilidade total). O corpo e JSON
  (`status` + `checks.<nome>.status/descricao`, sem excecao) e o alerta do broker fica por METRICA
  (`outbox_mensagens_pendentes`). `ProntidaoTests` (Infra) PARA o RabbitMQ e prova 200 + `rabbitmq: Degraded` +
  escrita aceita (outbox com 1 mensagem), e PARA o Mongo e prova 503 com `/health` em 200
- **Cache da prontidao (`HealthCheckComCache`)**: `/health/ready` e anonimo e, atras do wildcard da APIM, qualquer cliente da
  internet o alcanca; sem cache cada requisicao abriria uma conexao AMQP e um ping no Atlas (ate 3 s cada). Os checks `mongodb` e
  `rabbitmq` ficam atras do cache (`AddCheckComCache`, um estado por registro): o resultado vale
  `Health__ProntidaoCacheSegundos` (padrao **5 s**; 0 ou negativo desliga, e os testes de infraestrutura usam 0 para ver cada
  queda na hora). As requisicoes simultaneas esperam UMA avaliacao (trava) e excecao/cancelamento NUNCA e guardado: os dois checks
  deixam o cancelamento do chamador subir (`catch ... when (!cancellationToken.IsCancellationRequested)`), senao uma requisicao
  abortada no meio gravaria um 503/`Degraded` falso por 5 s. O custo fica limitado a 1 ping no Atlas + 1 conexao AMQP a cada 5 s,
  qualquer que seja a taxa de requisicoes; o cache e MENOR que o `periodSeconds` da readiness (30 s), entao cada probe do kubelet
  ve uma medicao nova. O `RabbitMqHealthCheck` entrega a tentativa abandonada (limite de 3 s OU cancelamento do chamador) ao
  fechamento em segundo plano, para um broker congelado que completar o handshake depois nao vazar a conexao. O corpo
  (`ProntidaoResponseWriter`) sai com `UnsafeRelaxedJsonEscaping` (acentos legiveis; e JSON puro, nunca HTML)
- **Partida com broker fora (relevante para o deploy)**: com `WaitUntilStarted = true` e o broker inalcancavel no boot a API nao
  escuta, SEM limite de tempo (medido: apos mais de 3 min nenhum "Bus started", nenhum crash, `/health` sem
  resposta). Uma `startupProbe` finita mata o pod e ele entra em CrashLoopBackOff ate o broker voltar: risco aceito
  para o primeiro deploy. `WaitUntilStarted = true` tambem impede subir um pod NOVO durante uma queda do broker (trade-off
  documentado: pods ja de pe seguem atendendo, por causa do outbox)
- `MassTransitIntegrationEventPublisher` so faz `Publish` e copia o `MessageId` do contrato para o envelope
  (delegate compilado e cacheado por tipo)

### Metricas

Meter `OficinaMecanica.Execucao` (registrado no OTel em `AddMeter`). Contadores (via `MetricasPendentes`, um buffer
por escopo): no caminho HTTP so contam depois do commit (descartados quando a transacao aborta e reexecuta); no
consumer o `MongoUnitOfWork` os publica ANTES do commit do outbox do MassTransit (o ADR 0002 admite): se esse
commit falhar a mensagem e reprocessada e a metrica conta de novo (metrica operacional, nao contabil).
`ordens_enfileiradas_total`, `diagnosticos_concluidos_total`,
`reparos_finalizados_total`, `ordens_inviabilizadas_total`, `cancelamentos_previos_total`. Gauges
observaveis (retrato atualizado a cada `Metricas:IntervaloDeAtualizacaoEmSegundos`, padrao 15 s, por
`ObterResumoDaFilaQuery` e `MongoIdadeDaFilaReader`): `fila_ordens{status}` e
`fila_idade_mais_antiga_segundos{status}` (alerta de SLA de diagnostico e reparo: SO metrica, nada e
cancelado; os 4 status com SLA sempre reportam, 0 quando vazios, para nao congelar a serie no Prometheus) e
`outbox_mensagens_pendentes` (mensagens ainda nao entregues ao broker: o alerta de broker fora, ja que a readiness
nao o cobre). No `/metrics` (prometheus-net) saem com o prefixo `oficinamecanica_execucao_`

### Auth

JWT RS256 validado por JWKS (`JwtSettings:Issuer|Audience|JwksUri`), sem emitir token. `RequireHttps` so
quando o `JwksUri` e https. `MapInboundClaims = false`: o mecanico e o claim `sub`. Se o `kid` do token nao
esta no cache do JWKS, o resolvedor pede refresh ao gerenciador antes de recusar (rotacao de chave)

## API

Todos `[Authorize]`; o `mecanicoId` vem SEMPRE do claim `sub` (campo de corpo e ignorado):

| Endpoint | Resposta |
|---|---|
| `POST /api/diagnosticos/proximo` | 200 ordem reivindicada, 204 fila vazia, 409 disputa esgotada |
| `POST /api/reparos/proximo` | idem para a fila de reparo |
| `POST /api/ordens-de-producao/{id}/apontamentos` | 200 |
| `POST /api/ordens-de-producao/{id}/diagnostico/concluir` | 200 (publica `DiagnosticoConcluido`) |
| `POST /api/ordens-de-producao/{id}/reparo/finalizar` | 200 (publica `ExecucaoFinalizada`) |
| `POST /api/ordens-de-producao/{id}/reparo/inviabilizar` | 200 (publica `ExecucaoInviavel`) |
| `GET /api/ordens-de-producao/{id}` | 200 / 404 (consulta sincrona do OS Service) |
| `GET /api/fila?status=&mecanicoId=&pagina=&tamanho=` | 200 paginado |
| `GET /api/fila/resumo` | 200 contagem por status |

`ExceptionHandlingMiddleware`: `DomainException`/`ValidationException` -> 400 (pt-BR), `KeyNotFoundException`
-> 404, `ConcorrenciaException` -> 409, sem `sub` -> 401, demais -> 500 sem detalhes. Corpo/JSON invalido e
query invalida tambem saem no formato `ErrorResponse` (a obrigatoriedade dos campos e do FluentValidation).

**Cliente que abandona a requisicao** (`RequestAborted` + `OperationCanceledException`, resposta ainda nao
iniciada) -> **499 sem corpo**, log **Information** so com o TEMPLATE da rota (`RoutePattern.RawText`, ou
`(rota desconhecida)`) e o trace id, nunca o path, a query nem o token: e seguranca por construcao (nas
rotas da Execucao o path so leva o GUID da ordem, mas o log nao depende disso). Cancelamento com o cliente
ainda conectado (timeout interno de dependencia) continua 500 com log Error. Com a resposta ja iniciada
(`HasStarted`) o middleware NAO toca em status nem cabecalhos (no Kestrel lancaria `InvalidOperationException`):
registra o erro (Error) e aborta a conexao (`context.Abort()`; o cliente ve a falha em vez de uma resposta
truncada que parece completa). Se foi o proprio cliente que fechou (`RequestAborted` ja cancelado +
`OperationCanceledException`) a conexao ja esta fechada: so Information, sem abortar. `UseHttpMetrics()`
vem ANTES do middleware no `Program.cs`, entao o `/metrics` conta 499/400/404/409/500 com o `code` real
(por dentro, tudo sairia com `code="200"`); `MetricasHttpTests` trava a ordem no pipeline real.

**Claim abandonado** (medido contra MongoDB e RabbitMQ reais; o cliente que larga a conexao nao sabe se o
claim valeu):
- Diagnostico (`POST /api/diagnosticos/proximo`, com a transacao do outbox): cancelamento antes do commit
  aborta a transacao (a ordem fica em `Fila`, outbox com 0 mensagens, nenhum evento); com o commit ja enviado
  ao servidor ele pode completar ("resultado desconhecido", ver ADR 0002): a ordem vai para `EmDiagnostico`
  com o mecanico e sai exatamente 1 `DiagnosticoIniciado`, so a resposta se perde. Sem estado parcial em 210
  iteracoes.
- Reparo (`POST /api/reparos/proximo`, sem transacao, um unico `ReplaceOne`): a escrita pode ser aplicada sem
  o mecanico receber a resposta (139/150 e 56/60 nos abandonos medidos).
- Recuperar: `GET /api/fila?status=EmDiagnostico&mecanicoId=<sub>` (diagnostico) ou
  `GET /api/fila?status=EmExecucao&mecanicoId=<sub>` (reparo). Limitacao conhecida: repetir o POST as cegas
  reivindica OUTRA ordem e a primeira fica parada com o mecanico (nao ha timer de reparo que a devolva). O
  cliente deve consultar a fila antes de repetir.
`/health` (anonimo) so confirma que o processo esta de pe (liveness, sem dependencias); `/health/ready` e a
readiness: MongoDB `Unhealthy` -> 503 (Atlas instavel tira o pod da rotacao em vez de reinicia-lo), broker e bus
`Degraded` -> 200 (ver "Prontidao" em Mensageria). `/metrics` anonimo
(prometheus-net). Swagger na raiz `/` (`CustomOperationIds` `controller_action`, Bearer, `servers[]` por
requisicao com `X-Forwarded-Prefix`). OTel (traces ASP.NET/HttpClient/`MassTransit`, metricas, Meter do
servico) e sink OTLP do Serilog so com `Otel:Endpoint`; a temporalidade das metricas sai por
`OTEL_EXPORTER_OTLP_METRICS_TEMPORALITY_PREFERENCE=delta` (ENV da imagem e do compose)

## Configuracao (variaveis `Secao__Chave`)

| Chave | Uso |
|---|---|
| `ConnectionStrings__MongoDb` | obrigatoria; replica set (`?replicaSet=rs0`); Atlas: segredo no Key Vault |
| `MongoDb__Database` | padrao `execucao` |
| `JwtSettings__Issuer` / `Audience` / `JwksUri` | validacao do JWT do Seguranca |
| `RabbitMq__Host` / `Port` / `VirtualHost` / `Username` / `Password` | broker (sem credencial padrao no codigo) |
| `Mensageria__Transporte` / `Outbox` | `RabbitMq`/`EmMemoria`, `MongoDb`/`Nenhum` (so Development e Testing trocam; ver guarda de producao) |
| `Mensageria__Retentativas` / `IntervaloInicialEmMs` / `IntervaloMaximoEmMs` | retry do consumer (5, 200, 30000) |
| `Mensageria__IntervaloDoOutboxEmMs` / `JanelaDeDeduplicacaoEmMinutos` | polling do outbox (1000) e dedup (30) |
| `Metricas__IntervaloDeAtualizacaoEmSegundos` | gauges da fila (15) |
| `Health__ProntidaoCacheSegundos` | cache do resultado de `/health/ready` (`mongodb`, `rabbitmq`) em segundos (5); 0 desliga (so testes de infraestrutura) |
| `Otel__Endpoint` | vazio desliga OTLP |

## Docker e execucao local

`docker-compose.yml` e self-contained: `api` (build local, porta 5002 -> 8080) e `mongo` (mongo:7.0.45 como
replica set de 1 no; o healthcheck inicia o `rs.initiate` com host `mongo:27017` e so fica saudavel quando o
no e PRIMARY; volume nomeado). Ferramentas no host conectam em `localhost:27018` com `directConnection=true`.
As portas do Mongo e do RabbitMQ standalone sao publicadas so em `127.0.0.1` (sem autenticacao e com
`--bind_ip_all`, nao podem ficar em todas as interfaces da maquina).
O `rabbitmq` (rabbitmq:4.4.0-management) so sobe com `--profile standalone` (publica 5673/15673; o dono do
broker compartilhado e o compose do OS Service, 5672/15672): nesse caso `RABBITMQ_HOST=rabbitmq` e
`RABBITMQ_PORT=5672` no `.env`. Containers `oficinamecanica-execucao-*` (prefixo trocavel por
`EXECUCAO_PREFIXO`); portas por `EXECUCAO_API_PORT`, `EXECUCAO_MONGO_PORT`, `EXECUCAO_RABBITMQ_PORT`,
`EXECUCAO_RABBITMQ_MGMT_PORT`. Copie `.env.example` para `.env` e preencha `RABBITMQ_USER`/`PASS`.

```bash
docker compose --profile standalone up -d --build      # API + Mongo + RabbitMQ proprios
curl http://localhost:5002/health                       # 200; Swagger em http://localhost:5002/
docker compose --profile standalone down -v
```

Pegadinha: `host.docker.internal` (JWKS e broker externos) pode resolver para IPv6 inalcancavel no Docker
Desktop; nesse caso use o IP do host em `JWKS_URI`/`RABBITMQ_HOST`. No Git Bash do Windows, `docker run -v`
precisa de `MSYS_NO_PATHCONV=1` para nao converter o caminho do volume.

## Testes e cobertura

```bash
dotnet build -c Release
dotnet test --filter "Category!=Infra"     # sem Docker (unitarios, API com fakes, consumers, mapeamento BSON)
dotnet test --filter "Category=Infra"      # Testcontainers: Mongo 7.0.45 (replica set) + RabbitMQ 4.4.0; precisa de Docker
dotnet test                                # tudo
```

Testes `Category=Infra` (todos contra Mongo e RabbitMQ reais): fluxo ponta a ponta, outbox atomico (falha
depois de gravar e publicar => 0 documentos alterados e 0 eventos), mensagem duplicada (MessageId repetido e
copias concorrentes) processada 1x, claim concorrente (6 mecanicos x 24 ordens, nunca a mesma ordem, FIFO por
mecanico, 1 evento por claim), fila de reparo ordenada por `LiberadaEm`, **corrida Enfileirar x CancelarProducao**
(varias rodadas, com encontro forcado dos dois handlers e sem; nunca sobra ordem na `Fila`; trocar tombstone e
ordem para colecoes diferentes faz o teste falhar), release idempotente, retry de `TransientTransactionError`
(conflito de escrita provocado de verdade; reexecucao e esgotamento -> 409), indices (inclusive `explain` sem
SORT em memoria), metricas. Sem paralelismo entre classes de teste (Meter estatico e containers compartilhados).

Cobertura de linhas (coverlet OpenCover + uniao no ReportGenerator, TODOS os projetos de teste incluindo Infra):

```bash
bash scripts/cobertura.sh      # roda cada projeto de teste uma vez (coverage.runsettings: so os assemblies do servico), une e aplica o gate
```

O script aceita uma fase (`testes-sem-infra`, `testes-infra`, `relatorio`), que e como o CI o chama; as saidas ficam em
`TestResults/` (`report-bruto/` e `report-com-exclusoes/`, ambos com `Summary.txt` e Html, e `resumo-cobertura.md`). O gate
(`COBERTURA_MINIMA`, padrao 80) vale para o numero "com exclusoes".

Meta: >= 80% de linhas do servico inteiro (Contracts, Domain, Application, Infrastructure e API), com **gate no CI**
(`scripts/verificar-cobertura.sh`) sobre o numero "com exclusoes". Exclusoes documentadas (nenhuma e necessaria para a meta):
`Program.cs` e `DependencyInjection*.cs` (bootstrap), no relatorio, e, na coleta (`coverage.runsettings`, o mesmo do Billing), o codigo
marcado com `GeneratedCode` ou `ExcludeFromCodeCoverage`. `CompilerGenerated` NAO e excluido: tiraria iteradores, lambdas e maquinas
de estado async e inflaria a medida. O SonarCloud usa a mesma lista de arquivos (`sonar.coverage.exclusions`).

Ultima medicao (2026-11-08, `bash scripts/cobertura.sh`, todos os projetos de teste, incluindo Infra): **bruto 99,42%** (1899 de 1910
linhas); **com as exclusoes 99,42%** (1718 de 1728), que e o numero do gate. Por assembly (com exclusoes): Contracts, API e Domain 100%,
Application 99,7%, Infrastructure 98,6%.
`Program.cs` entra coberto porque os testes de API sobem a aplicacao inteira. Testes: Domain 185, Application 162, Contracts 23,
Integration 291 (238 sem Docker + 53 `Category=Infra`), 661 no total.

## CI/CD e Kubernetes

Decisoes no ADR 0003. Pipeline proprio em `.github/workflows/ci.yml` (nome `CI`; gatilhos push/PR em `main`/`release` e
`workflow_dispatch`; `concurrency` por ref, `cancel-in-progress` so em PR; `permissions` minimas, `id-token: write` so nos jobs
de imagem/deploy). Todas as actions com versao pinada por **tag de major/minor** (mutavel: pinar por SHA e BLOQUEANTE da publicacao, ver abaixo). O bloco `env:` do topo do `ci.yml` concentra o que muda de um servico
para outro (ACR, imagem, AKS, pastas, solucao). Cinco jobs encadeados. **Os nomes dos jobs/checks (`Build & Test`, `Quality`) sao identicos aos dos outros
repositorios** porque o ruleset exige o mesmo nome em todos.

| Job (`name`) | Quando | O que faz |
|---|---|---|
| `build-and-test` (**`Build & Test`**, check obrigatorio) | sempre | restore, build Release, `testar-scripts.sh`, `verificar-masstransit.sh`, `validar-k8s.sh` (kubeconform), `exportar-swagger.sh --verificar`, testes `Category!=Infra` e `Category=Infra` (Testcontainers: MongoDB em replica set + RabbitMQ), `cobertura.sh relatorio` (merge + gate), `dorny/test-reporter` com os `.trx`, Job Summary de cobertura, artifacts `openapi.json`, `relatorio-de-cobertura`, `resultados-dos-testes` e `cobertura-opencover` |
| `quality` (**`Quality`**, check obrigatorio) | `needs: build-and-test`; pulado em PR de fork | SonarCloud (`dotnet-sonarscanner` begin/build/end), importa o OpenCover do artifact, `sonar.qualitygate.wait=true`; falha com mensagem clara se faltar `SONAR_TOKEN`/variavel |
| `scan-image` | so push/dispatch em `main`/`release`, `needs: [build-and-test, quality]` | **so `contents: read`, sem `id-token`**: build local da imagem (buildx, cache gha), **Trivy** (HIGH/CRITICAL com correcao, `ignore-unfixed`) e `docker save` como artifact (`imagem-escaneada`, 1 dia). As actions de terceiros ficam aqui, longe da identidade do Azure |
| `build-and-push-image` | idem, `needs: scan-image` | OIDC (`id-token: write`), baixa o artifact, `docker load`, `az acr login` e empurra a MESMA imagem escaneada (sem rebuild); tags `oficinamecanica-execucao:<sha>` sempre e `:latest` so na `main`. Sem checkout; so `actions/*` e `azure/login` |
| `deploy-to-aks` | idem, `needs: build-and-push-image`; `concurrency: deploy-aks-execucao` (`cancel-in-progress: false`) | OIDC, `aks-set-context` (admin, `rgfiap`/`aksfiap`), `envsubst '${IMAGE}'`, `kubectl apply`, `set image`, `rollout status` (420 s). Namespace `default`. **Sem migration** (MongoDB nao tem esquema): os indices e as colecoes do outbox/inbox sao criados pelo `MongoInitializer` na partida de cada pod |

**O que o repositorio do GitHub precisa ter (Settings > Secrets and variables > Actions) quando existir:**

| Tipo | Nome | Valor |
|---|---|---|
| variavel | `AZURE_CLIENT_ID` | `terraform output execucao_github_actions_client_id` do `OficinaMecanica.Infra` (so existe depois de preencher `execucao_github_repo_id`) |
| variavel | `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` | os mesmos dos demais repositorios |
| variavel | `SONAR_PROJECT_KEY`, `SONAR_ORGANIZATION` | projeto e organizacao no SonarCloud |
| secret | `SONAR_TOKEN` | token do SonarCloud; e o **unico** secret de longa duracao (o SonarCloud nao suporta OIDC). Criar tambem em Settings > Secrets > **Dependabot**: os PRs do Dependabot nao enxergam os secrets de Actions e reprovariam em `Quality` |

Nenhum segredo do Azure: o login e OIDC (a identidade so aceita `push` em `main`/`release`, por isso `Build & Test` e `Quality` em PR nunca
tocam o Azure). A connection string do Atlas e as credenciais do RabbitMQ vivem so no Key Vault e chegam ao pod pelo CSI; o pipeline
nao as le.

**Scripts** (`scripts/`, todos com `bash`; o `ci.yml` e o `ci-local.sh` chamam os mesmos):

| Script | Funcao |
|---|---|
| `ci-local.sh [--sem-infra]` | roda localmente os mesmos passos do `Build & Test` (restore, build, testes dos scripts, guardrail, k8s, OpenAPI, testes, cobertura com gate); `--sem-infra` pula o Testcontainers e, sem a cobertura completa, nao avalia o gate |
| `cobertura.sh [fase]` | `tudo` (padrao), `testes-sem-infra`, `testes-infra`, `relatorio`; cada projeto roda uma vez com OpenCover + `.trx`, com `--blame-hang-timeout 5m --blame-hang-dump-type none` (rede de seguranca: um teste pendurado vira falha em vez de travar o run ate o `timeout-minutes` do job; o `ci-local.sh` nao tem timeout); `COBERTURA_MINIMA` (80) e `COBERTURA_SEM_BUILD=true` (CI) |
| `verificar-cobertura.sh <Summary.txt> [min]` | o gate: calcula o percentual de `Covered lines`/`Coverable lines` (inteiros, nao o `Line coverage` arredondado) e falha (exit 1) abaixo do minimo; vale para o numero "com exclusoes" |
| `verificar-masstransit.sh [alvo]` / `--entrada <arquivo>` | falha se qualquer pacote `MassTransit*` (inclui `MassTransit.MongoDb`), direto ou transitivo, resolver para major diferente de 8; `--entrada` analisa uma saida ja capturada de `dotnet list package` (sem restore) |
| `verificar-workflow.sh [ci.yml]` | guarda a separacao de privilegios do workflow: job que declara `id-token` so pode executar `actions/*` e `azure/*` (as de terceiros ficam no `scan-image`, sem `id-token`); `id-token` no nivel do workflow tambem reprova (exit 1; 2 se o arquivo nao tem jobs) |
| `testar-scripts.sh [--sem-mutantes]` | testa o caminho de FALHA de TODOS os scripts de CI com entradas sinteticas (72 casos) e planta defeitos em copias dos scripts exigindo que cada um seja detectado (31 mutantes; dentro de um mutante o primeiro caso que reprova basta). Cobre: o gate e o guardrail isolados; o valor do gate no CI (o `ci.yml` exporta `COBERTURA_MINIMA: "80"`, com mutante 80 -> 10); o verificador do workflow; o **`cobertura.sh relatorio` de ponta a ponta** sobre um OpenCover sintetico (79% reprova, 81% aprova, `Program.cs` e `DependencyInjection.cs` fora do numero do gate, cada filtro de exclusao com seu mutante, o aviso de `[ExcludeFromCodeCoverage]` com a contagem no resumo, `COBERTURA_MINIMA`, fase invalida, sem relatorios), o que prova que o gate esta LIGADO ao relatorio (um mutante apaga a chamada); `validar-k8s.sh` (diretorio inexistente, campo desconhecido reprova no `-strict`, CRD ignorado; precisa de Docker, pulado com aviso sem ele); `exportar-swagger.sh --verificar` com um `dotnet` de mentira no `PATH` (em dia aprova, divergente e ausente reprovam, normalizacao de CRLF e `\r\n`); `--x` em `exportar-swagger.sh` e `ci-local.sh` (exit 2); e que `ci.yml` e `ci-local.sh` chamam os mesmos passos. Tudo que escreve roda numa copia temporaria (sandbox), nunca em `TestResults/`, `artifacts/` ou `docs/` do repositorio. Roda no `Build & Test` e no `ci-local.sh`; leva ~2,5 a 3 min (medido: de 2 min 39 s a 3 min 1 s, conforme a carga da maquina; o peso e o ReportGenerator e o kubeconform em Docker) |
| `exportar-swagger.sh [--verificar] [--sem-build]` | `swagger tofile` (ferramenta fixada em `dotnet-tools.json`) para `docs/openapi.json`; com `--verificar` gera em `artifacts/openapi.json` e falha se o versionado difere |
| `validar-k8s.sh` | renderiza `${IMAGE}` e valida `k8s/execucao/` com `kubeconform -strict` (Docker; o CRD do SecretProviderClass e ignorado) |
| `aplicar-ruleset.sh [owner/repo] [--dry-run]` | cria/atualiza o ruleset via `gh api`; **so roda na publicacao** |

- O `swagger tofile` executa `Program.Main` e ESPERA o host subir, e a partida cria os indices no MongoDB: o `exportar-swagger.sh` sobe um
  `mongod` descartavel em Docker (`mongo:7.0.45`, porta aleatoria em `127.0.0.1`, removido ao sair) ou usa `SWAGGER_MONGO_URI`, e roda a API
  em `Testing` com transporte em memoria e sem outbox. O documento e o mesmo de `Production` (a API nao tem rota por ambiente). O arquivo e
  gravado com LF, inclusive o `\r\n` que o compilador deixa dentro das strings dos comentarios XML multilinha no Windows (verificado: o
  documento gerado no Linux e identico ao do Windows). `.gitattributes` fixa `docs/openapi.json` e `*.sh` em LF.
- **Protecao de branch**: `.github/rulesets/protect-main-release.json` (alvo `main` e `release`; `deletion`, `non_fast_forward`,
  `pull_request` com 0 aprovacoes e merge/squash/rebase, `required_status_checks` com `Build & Test` e `Quality`). Replica o ruleset dos
  outros repositorios (aqui sem acesso ao GitHub para comparar: confirmar na publicacao).
- **Dependabot**: nuget, github-actions e docker, semanal; ignora major de `MassTransit*` (9.x e comercial), de `MongoDB.Driver*` (fica na
  versao do `MassTransit.MongoDb 8.5.12`) e das imagens base `dotnet/aspnet`/`dotnet/sdk`.

**`k8s/execucao/`** (namespace `default`, nomes `oficinamecanica-execucao`, container `api`, Secret `execucao-secrets`):
- `deployment.yaml`: sem `replicas` (o HPA manda: 1 a 2), `maxSurge: 0`/`maxUnavailable: 1` (no unico sem CPU para pod extra; com uma replica ha
  uma janela sem pod no rollout, e a saga espera no RabbitMQ). Unico placeholder `${IMAGE}` (o CI renderiza com `envsubst`; aplicar
  sem renderizar falha com nome de imagem invalido de proposito). `runAsNonRoot` (uid 1654, o usuario `app` da imagem),
  `readOnlyRootFilesystem` com `emptyDir` em `/tmp`, `capabilities: drop ALL`, `automountServiceAccountToken: false`.
  Requests `50m`/`192Mi`, limits `300m`/`384Mi`: CPU e o recurso escasso do no; medido no container (MongoDB em replica set e RabbitMQ
  reais, 0,3 CPU), a API usa ~82 MiB em repouso e ~110 MiB depois de ~600 requisicoes e 3 mensagens consumidas.
- **Variaveis**: `ASPNETCORE_ENVIRONMENT=Production` (nunca `Development`/`Testing`: o validador recusa transporte em memoria e outbox
  `Nenhum`), `Mensageria__Transporte=RabbitMq`, `Mensageria__Outbox=MongoDb`, `RabbitMq__Host/Port/VirtualHost`
  (`rabbitmq.messaging.svc.cluster.local:5672`), `MongoDb__Database=execucao`, `JwtSettings__Issuer/Audience/JwksUri` (JWKS pela APIM,
  `segurancaserver`), `Otel__Endpoint` (Collector) e `OTEL_EXPORTER_OTLP_METRICS_TEMPORALITY_PREFERENCE=delta`. Por `secretKeyRef` do Secret
  `execucao-secrets`: `ConnectionStrings__MongoDb`, `RabbitMq__Username`, `RabbitMq__Password`. Os ajustes finos de `Mensageria__*` e
  `Metricas__*` ficam nos padroes do codigo.
- **Probes**: `startupProbe` em `/health` de **5 minutos** (60 x 5 s): com o broker inalcancavel no boot o `WaitUntilStarted` segura a
  partida sem limite e a API nao abre a porta; passado o prazo o kubelet reinicia o pod, que fica em `CrashLoopBackOff` ate o RabbitMQ voltar
  (risco aceito, ver "Partida com broker fora"). `livenessProbe` em `/health` (liveness puro, `timeoutSeconds: 5`, `periodSeconds: 30`) e
  `readinessProbe` em `/health/ready` (MongoDB 503, broker `Degraded` 200; `periodSeconds: 30`, `timeoutSeconds: 5`: os checks tem limite de
  3 s, valido tambem com o broker CONGELADO, e o padrao do Kubernetes, 1 s, reprovaria ate a prontidao saudavel). O resultado da prontidao fica
  em cache de 5 s (`Health__ProntidaoCacheSegundos`, menor que o periodo: cada probe ve uma medicao nova) porque o endpoint e anonimo e a APIM o
  repassa. O periodo de 30 s reduz so a carga das probes (uma conexao AMQP e um ping no Atlas por avaliacao, no maximo uma por janela do cache); nao
  evita trafego que o servico ja gera (o outbox consulta o MongoDB a cada `IntervaloDoOutboxEmMs`) e tem o custo de ate 90 s para tirar um pod da
  rotacao.
- `ingress.yaml`: prefixo `/execucao(/|$)(.*)` com `use-regex` + `rewrite-target: /$2` (`ImplementationSpecific`); o monolito segue catch-all em `/`.
  **Sem** `x-forwarded-prefix`: o `X-Forwarded-Prefix: /execucaoserver` vem da policy da APIM e o ingress o repassa.
- `secret-provider-class.yaml`: os 3 secrets do Key Vault `kvfiap` (`execucao-mongo-connection-string`, gravado pelo `OficinaMecanica.Banco`;
  `rabbitmq-username` e `rabbitmq-password`) sincronizados no Secret `execucao-secrets`; identidade e tenant sao os mesmos literais do monolito.
- `kubectl apply --dry-run=client` nao roda sem API server (precisa de discovery); a validacao offline e o `kubeconform -strict`.

**Validado localmente (sem Azure/GitHub)**: `actionlint` e `shellcheck` sem achados, `kubeconform -strict` (4 validos, 1 CRD ignorado), imagem
construida e executada como nao-root com `--read-only`, `--cap-drop ALL`, 384 MiB e 0,3 CPU contra MongoDB (replica set) e RabbitMQ reais
(`/health` 200 em ~6-8 s, `/health/ready` 200 JSON, mensagens consumidas e indices criados; com o broker congelado `/health/ready` 200
`Degraded` em 3 s, com o MongoDB congelado 503 em 3 s), `testar-scripts.sh` (72 casos e 31 mutantes detectados; falha tambem quando os scripts sao
quebrados numa copia fora do repositorio), guardrail reprovando uma copia com `MassTransit 9`, OpenAPI gerado e conferido (inclusive em Linux, com
`mawk`), gate de cobertura passando com o relatorio real e reprovando com limite acima do medido, `ci-local.sh` completo.

**Clonar para outro servico (OS Service)**: trocar o bloco `env:` do `ci.yml`; os nomes de projeto/DLL no topo de `exportar-swagger.sh` (e o
bloco do MongoDB, se o servico nao precisar de banco na partida), os projetos e filtros de `cobertura.sh` e `coverage.runsettings`,
`K8S_DIR`/imagem em `validar-k8s.sh`, o repo padrao do `aplicar-ruleset.sh`; os nomes, labels e a lista de secrets dos manifests; o
MassTransit/Mongo/EF do guardrail e do Dependabot conforme o servico (servico com EF precisa do passo de migration no deploy).

**`[ExcludeFromCodeCoverage]` e proibido sem ADR**: o coverlet o honra (`coverage.runsettings`) e o codigo marcado some, em silencio, das duas
medicoes (nem entra no denominador); `cobertura.sh` lista as ocorrencias em `src/` no relatorio e no Job Summary (hoje nenhuma).

**BLOQUEANTE antes do primeiro push real**: fixar as actions de terceiros (`docker/*`, `aquasecurity/trivy-action`, `dorny/test-reporter`,
`azure/login`, `azure/aks-set-context`) por sha COMPLETO de commit verificado (tag e mutavel), registrado no ADR 0003; hoje estao por tag.

**Fica para a publicacao**: criar o repositorio `willianrmattos/OficinaMecanica.Execucao` e empurrar; preencher `execucao_github_repo_id` no
`OficinaMecanica.Infra` (aplicar) e configurar as variaveis OIDC; criar o secret `SONAR_TOKEN` e as variaveis do Sonar (e o projeto no
SonarCloud); rodar uma vez o CI para os checks existirem e entao `bash scripts/aplicar-ruleset.sh`; cumprir o item BLOQUEANTE acima (actions
de terceiros por sha completo, conferindo antes as versoes de `aquasecurity/trivy-action` e `dorny/test-reporter`); confirmar a premissa da
APIM (concatenacao do path do backend `/execucao`); `kubectl describe node` com folga de CPU antes do primeiro deploy; confirmar que o egress do AKS esta na
allowlist do Atlas e que o secret `execucao-mongo-connection-string` ja existe no Key Vault (`OficinaMecanica.Banco` aplicado antes).

## Contratos

`src/OficinaMecanica.Contracts` e `tests/OficinaMecanica.Contracts.Tests` sao
copia exata (mesmos arquivos e namespaces `Commands`/`Events`/`Common`) do
subconjunto usado por este servico, a partir de `OficinaMecanica`; nao editar
aqui, mudanca de contrato nasce no `OficinaMecanica` e e recopiada. Os goldens
JSON devem ter bytes identicos aos do repositorio de origem (`.gitattributes`
fixa LF). `AmostrasDeContratos.cs` e filtrado para o subconjunto, e
`Common/` e copiado inteiro, mesmo o `TipoPrazo`, que aqui nao e usado (so serve
a `PrazoExpirado`, que esta fora do subconjunto). O unico outro desvio e o
`Contracts.Tests.csproj`: `Microsoft.NET.Test.Sdk` e as duas dependencias fixadas
descritas em "NuGet", iguais aos outros projetos de teste deste repositorio.

## Para a saga do OS Service

- `SolicitarDiagnostico`/`IniciarExecucao`/`CancelarProducao` vao para o endpoint `execucao-comandos`
  (`Filas.Execucao`); o `MessageId` do contrato e o `MessageId` do envelope (o inbox deduplica por ele)
- `CancelarProducao` em `EmExecucao`, `Finalizada` ou `Inviavel` e `DomainException`: vai direto para
  `execucao-comandos_error` (sem retry) e a saga recebe o `Fault<CancelarProducao>`. Producao em reparo so se
  desfaz por `ExecucaoInviavel`
- `IniciarExecucao` antes do diagnostico concluir e `DomainException` (vai para `_error`); para ordem que
  ainda nao existe e `KeyNotFoundException`, retentada pelo backoff antes de ir para `_error`
- `DiagnosticoIniciado` so sai quando um mecanico reivindica a ordem (nao ao enfileirar); `ExecucaoInviavel` pode
  sair de `AguardandoExecucao` (qualquer mecanico identificado) e de `EmExecucao`
- `GET /api/ordens-de-producao/{id}` e a consulta sincrona (404 se nao existe ou se so ha tombstone); repassar o JWT

## Comandos Uteis

```bash
dotnet build -c Release
dotnet test
docker compose --profile standalone up -d --build
bash scripts/ci-local.sh           # os mesmos passos do job "Build & Test" do CI (--sem-infra se nao ha Docker)
bash scripts/cobertura.sh          # cobertura mesclada com gate; aceita fase: testes-sem-infra | testes-infra | relatorio
bash scripts/exportar-swagger.sh   # regera docs/openapi.json (rode e versione quando a API mudar; precisa de Docker)
bash scripts/validar-k8s.sh        # kubeconform nos manifests (Docker)
```

## NuGet

`nuget.config` local aponta so para `nuget.org` (mesmo do `OficinaMecanica`).
Ao adicionar pacotes:
```bash
dotnet add <projeto> package <pacote> --source https://api.nuget.org/v3/index.json
```
`MassTransit`, `MassTransit.RabbitMQ` e `MassTransit.MongoDb` fixos em `8.5.12` (versao exata; a 9.x e
comercial e NUNCA entra) e `MongoDB.Driver` `3.12.0` (a que o `MassTransit.MongoDb 8.5.12` traz).
Serilog, OpenTelemetry, prometheus-net, Swashbuckle e JwtBearer nas versoes do monolito.
`dotnet-tools.json`: `dotnet-reportgenerator-globaltool` 5.5.12 e `swashbuckle.aspnetcore.cli` 6.5.0 (mesma versao do Swashbuckle da API).

Os projetos de teste usam `Microsoft.NET.Test.Sdk` 17.12.0 (como o
`OficinaMecanica.Seguranca`) e fixam `System.Net.Http` 4.3.4 e
`System.Text.RegularExpressions` 4.3.1: o `xunit` 2.4.2 (mesma versao do
monolito) arrasta essas duas via `NETStandard.Library` 1.6.1 em versoes com aviso
de vulnerabilidade High.

## Pendencias

- Teste real de transacao + consumer no Atlas M0 (credenciais do usuario); fallback do spike descrito no ADR 0001
- Decisao pendente no `OficinaMecanica.Banco` (README de la, "Decisao pendente: como o CI alcanca a API do Atlas"): sem ela o CI do Banco recebe 403 do Atlas, o job `validate` trava e o cluster que a Execucao usa nao e provisionado pelo CI
- Publicacao do CI/CD (repositorio no GitHub, OIDC, SonarCloud, ruleset, **sha das actions de terceiros**): lista em "CI/CD e Kubernetes"; o dashboard fica para a documentacao final
- Bloqueio na APIM (`OficinaMecanica.Infra`): `/health/ready` e `/metrics` sao anonimos e a rota `execucaoserver` da APIM repassa qualquer path. O custo de
  `/health/ready` ja esta limitado pelo cache de 5 s (no maximo 1 conexao AMQP e 1 ping no Atlas por janela, qualquer que seja a taxa de
  requisicoes); o cache nao esconde o endpoint nem o `/metrics`, entao o bloqueio dos dois paths na rota publica (policy `return-response`
  404 na APIM) foi escrito no Infra (ADR 0007 de la) e fica ativo quando o Terraform do Infra for aplicado (ainda nao foi)
- Atlas/Key Vault: `ConnectionStrings__MongoDb` e credenciais RabbitMQ so por segredo, nunca em arquivo versionado
