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
API, Dockerfile, docker-compose e testes (incluindo Testcontainers) prontos.
Ainda nao existem `k8s/` e CI/CD (fase F7), README final (F9) e o teste real no
Atlas M0 (precisa de credenciais do usuario).

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
    Messaging/     MensageriaOptions, MassTransitIntegrationEventPublisher, Consumers/ (SolicitarDiagnostico, IniciarExecucao, CancelarProducao)
    Observability/ MetricasExecucao (Meter), MetricasPendentes, MetricasDaFila (+ BackgroundService), MongoIdadeDaFilaReader
    Authentication/ JsonWebKeySetRetriever, ResolvedorDeChavesJwks, AutenticacaoJwtExtensions
    DependencyInjection.cs   # AddInfrastructure = AddPersistenciaMongo + AddMensageria + AddMetricasDaFila + AddAutenticacaoJwt
  OficinaMecanica.Execucao.API/             # Program.cs, Controllers (so traduzem HTTP <-> MediatR), ExceptionHandlingMiddleware,
                                             # Swagger/OTel/Serilog em Extensions/
tests/
  OficinaMecanica.Contracts.Tests/          # goldens JSON dos contratos copiados
  OficinaMecanica.Execucao.Domain.Tests/    # sem mocks
  OficinaMecanica.Execucao.Application.Tests/  # Moq, um arquivo por handler/validator
  OficinaMecanica.Execucao.Integration.Tests/  # sem Docker (mapeamento BSON, consumers, publisher, behavior, API via
                                             # WebApplicationFactory com fakes) + [Trait("Category","Infra")] com Testcontainers
  OficinaMecanica.Execucao.Tests.Common/    # builders Bogus (OrdemDeProducaoBuilder etc.) e RelogioFixo
docs/adr/                                    # 0001 MongoDB Atlas, 0002 outbox MongoDB do MassTransit + Unit of Work
Dockerfile, docker-compose.yml, .env.example, .dockerignore
dotnet-tools.json                            # ReportGenerator (merge de cobertura)
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
- Testes: xUnit + FluentAssertions (sem `Assert.*` puro) + Moq (so no
  Application.Tests e nos consumers do Integration.Tests, que mapeiam contrato -> command) +
  Bogus nos builders do `Tests.Common`; Domain.Tests nao usa mock
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
  Domain da onda 2): a reidratacao por `BsonClassMap` preenche o campo privado e o
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
  `Degraded` (HTTP 200) por `RabbitMqHealthCheck` (conexao AMQP real, limite de 3 s) e o check `masstransit-bus` do
  MassTransit tem a falha rebaixada para `Degraded`. Decisao de desenho (vale para OS, Billing e Execucao): com o
  outbox transacional o servico continua aceitando escrita com o broker fora (a mensagem espera no banco), entao
  broker caido nao pode derrubar a rotacao (readiness em cascata = indisponibilidade total). O corpo e JSON
  (`status` + `checks.<nome>.status/descricao`, sem excecao) e o alerta do broker fica por METRICA
  (`outbox_mensagens_pendentes`). `ProntidaoTests` (Infra) PARA o RabbitMQ e prova 200 + `rabbitmq: Degraded` +
  escrita aceita (outbox com 1 mensagem), e PARA o Mongo e prova 503 com `/health` em 200
- **Partida com broker fora (para a F7)**: com `WaitUntilStarted = true` e o broker inalcancavel no boot a API nao
  escuta, SEM limite de tempo (medido: apos mais de 3 min nenhum "Bus started", nenhum crash, `/health` sem
  resposta). Uma `startupProbe` finita mata o pod e ele entra em CrashLoopBackOff ate o broker voltar: risco aceito
  para a F7. `WaitUntilStarted = true` tambem impede subir um pod NOVO durante uma queda do broker (trade-off
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
dotnet tool restore
dotnet test tests/OficinaMecanica.Execucao.Domain.Tests      --collect:"XPlat Code Coverage" --results-directory TestResults/Domain      -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
dotnet test tests/OficinaMecanica.Execucao.Application.Tests --collect:"XPlat Code Coverage" --results-directory TestResults/Application -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
dotnet test tests/OficinaMecanica.Contracts.Tests            --collect:"XPlat Code Coverage" --results-directory TestResults/Contracts   -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
dotnet test tests/OficinaMecanica.Execucao.Integration.Tests --collect:"XPlat Code Coverage" --results-directory TestResults/Integration -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
# bruto (todos os assemblies do servico):
dotnet reportgenerator -reports:"TestResults/**/coverage.opencover.xml" -targetdir:TestResults/report -reporttypes:"TextSummary;Html" -assemblyfilters:"+OficinaMecanica.*;-OficinaMecanica.*.Tests;-OficinaMecanica.*Tests.Common"
# com as exclusoes documentadas:
dotnet reportgenerator ... -targetdir:TestResults/report-excl -filefilters:"-*Program.cs;-*DependencyInjection*.cs"
```

Meta: >= 80% de linhas do servico inteiro (Contracts, Domain, Application, Infrastructure e API). Exclusoes
documentadas (so estas, nenhuma e necessaria para a meta): `Program.cs` e `DependencyInjection*.cs` (bootstrap).

Ultima medicao (2026-11-07, todos os projetos de teste, incluindo Infra): **bruto 99,3%** (2060 de 2073 linhas);
**com as exclusoes 99,3%** (1868 de 1880). Por assembly: Contracts 100%, Domain 100%, API 100%, Application 99,8%,
Infrastructure 98,5%. `Program.cs` entra coberto porque os testes de API sobem a aplicacao inteira. Testes: Domain 185,
Application 162, Contracts 23, Integration 241 (190 sem Docker + 51 `Category=Infra`).

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

## Para a saga do OS Service (F3)

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

Os projetos de teste usam `Microsoft.NET.Test.Sdk` 17.12.0 (como o
`OficinaMecanica.Seguranca`) e fixam `System.Net.Http` 4.3.4 e
`System.Text.RegularExpressions` 4.3.1: o `xunit` 2.4.2 (mesma versao do
monolito) arrasta essas duas via `NETStandard.Library` 1.6.1 em versoes com aviso
de vulnerabilidade High.

## Pendencias

- Teste real de transacao + consumer no Atlas M0 (credenciais do usuario); fallback do spike descrito no ADR 0001
- `k8s/`, CI/CD, Trivy/SonarCloud (F7); README final e dashboard (F9)
- Atlas/Key Vault: `ConnectionStrings__MongoDb` e credenciais RabbitMQ so por segredo, nunca em arquivo versionado
