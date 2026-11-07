# OficinaMecanica.Execucao - Fila de Execucao, Diagnostico e Reparo

## Visao Geral

Microsservico (.NET 8) do ecossistema OficinaMecanica responsavel pela
producao na oficina: fila de ordens, diagnostico, reparo, apontamentos do
mecanico e laudo. Fala com o `OficinaMecanica` (OS Service, que hospeda a
saga) so por mensagens: recebe `SolicitarDiagnostico`, `IniciarExecucao` e
`CancelarProducao`; publica `DiagnosticoIniciado`, `DiagnosticoConcluido`,
`ExecucaoFinalizada` e `ExecucaoInviavel`. Nenhuma dependencia de compilacao
com outro repositorio e nenhum acesso ao banco de outro servico.

Estado atual: so as camadas `Domain` e `Application` (com `Contracts` e
testes). `Infrastructure` (MongoDB + MassTransit/RabbitMQ), `API`,
Dockerfile, `k8s/` e CI/CD ainda nao existem neste repositorio.

## Estrutura do Projeto

```
src/
  OficinaMecanica.Contracts/                # copia do subconjunto de mensagens usado aqui (ver "Contratos")
  OficinaMecanica.Execucao.Domain/          # OrdemDeProducao (aggregate), CancelamentoPrevio, value objects, enums,
                                             # repositorios (IOrdemDeProducaoRepository, ICancelamentoPrevioRepository) e IUnitOfWork
  OficinaMecanica.Execucao.Application/     # Commands/Queries (MediatR), Validators (FluentValidation), DTOs,
                                             # ValidationBehavior, IIntegrationEventPublisher
tests/
  OficinaMecanica.Contracts.Tests/          # goldens JSON dos contratos copiados
  OficinaMecanica.Execucao.Domain.Tests/    # sem mocks
  OficinaMecanica.Execucao.Application.Tests/  # Moq, um arquivo por handler/validator
  OficinaMecanica.Execucao.Tests.Common/    # builders Bogus (OrdemDeProducaoBuilder etc.) e RelogioFixo
```

Mesmos principios do `OficinaMecanica`: Clean Architecture (Domain ->
Application -> Infrastructure -> API), DDD, CQRS via MediatR,
`ValidationBehavior`, Repository pattern (interface no Domain).

## Convencoes

- **Idioma do dominio**: entidades, propriedades, enums, mensagens de erro e
  de validacao em portugues (com acento nas strings); namespaces, camadas e
  nomes de pattern em ingles
- Comentarios so quando explicam um "porque" nao obvio, em primeira pessoa
  ("optei por"), nunca em segunda pessoa
- Testes: xUnit + FluentAssertions (sem `Assert.*` puro) + Moq (so no
  Application.Tests) + Bogus nos builders do `Tests.Common`; Domain.Tests nao
  usa mock
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
  `mecanicoId` nao vazio). Quem inviabilizou fica sempre em `InviabilizadaPor`
  (em `AguardandoExecucao` nao e o `MecanicoId` da ordem). A API da onda 2
  restringe isso por papel
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
  assembly). Os requisitos da implementacao estao em "Requisitos da onda 2"

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

## Requisitos da onda 2 (Infrastructure)

O que a implementacao dos repositorios/UoW (MongoDB + outbox) precisa garantir; o
`Domain`/`Application` ja assumem isso (detalhes nos comentarios das interfaces):

- **`AtualizarAsync` grava na hora** e a `ConcorrenciaException` sai dele, nunca de
  `SaveChangesAsync` (o retry da reivindicacao nao cobre o commit); o UoW/sessao
  precisa seguir utilizavel depois da excecao
- **Releitura apos conflito**: `ObterProximaDaFilaAsync` depois de uma
  `ConcorrenciaException` precisa enxergar o estado commitado (nova
  transacao/snapshot), senao devolve a mesma ordem disputada
- **Claim + outbox na mesma transacao**: o claim de `ReivindicarProximoDiagnostico`/
  `ReivindicarProximoReparo` e a mensagem do outbox commitam juntos, senao a ordem
  pode ficar `EmDiagnostico` sem `DiagnosticoIniciado` publicado
- **Ordem da fila**: `EnfileiradaEm` na fila de diagnostico (`Fila`), `LiberadaEm` na
  de reparo (`AguardandoExecucao`)
- **Enfileirar x CancelarProducao (write skew)**: ordem e tombstone
  (`CancelamentoPrevio`) sao documentos distintos e a transacao Mongo (snapshot) NAO
  impede write skew entre eles: a saga manda `SolicitarDiagnostico` e depois
  `CancelarProducao` ao mesmo endpoint, entao as duas mensagens podem rodar em
  paralelo (T1 insere a ordem, T2 insere o tombstone, as duas commitam e sobra
  ordem orfa na `Fila`). A implementacao DEVE fazer as duas escritas colidirem na
  MESMA chave unica (mesma colecao com `_id` = `OrdemDeServicoId`, tombstone
  discriminado e ignorado por `ObterPorIdAsync` e pelas filas) OU serializar por
  `OrdemDeServicoId`. O conflito aborta a transacao do perdedor, o
  `UseMessageRetry` (antes do outbox) reexecuta o handler inteiro e a
  pre-checagem ja existente vira no-op
- **`AdicionarAsync` => `false`** so e alcancavel fora de transacao ou com upsert
  (`$setOnInsert`); dentro de transacao o E11000 aborta tudo e a excecao sobe. O
  retry de `TransientTransactionError` reexecuta o handler inteiro

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

## Comandos Uteis

```bash
# Build e testes
dotnet build -c Release
dotnet test

# Testes com cobertura (OpenCover; meta: >= 80% de linhas somando Domain + Application)
dotnet test tests/OficinaMecanica.Execucao.Domain.Tests --collect:"XPlat Code Coverage" --results-directory TestResults/Domain -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
dotnet test tests/OficinaMecanica.Execucao.Application.Tests --collect:"XPlat Code Coverage" --results-directory TestResults/Application -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
```

## NuGet

`nuget.config` local aponta so para `nuget.org` (mesmo do `OficinaMecanica`).
Ao adicionar pacotes:
```bash
dotnet add <projeto> package <pacote> --source https://api.nuget.org/v3/index.json
```
Ainda nao ha nenhum pacote `MassTransit*` nem `MongoDB.Driver` nesta base.

Os 3 projetos de teste usam `Microsoft.NET.Test.Sdk` 17.12.0 (como o
`OficinaMecanica.Seguranca`) e fixam `System.Net.Http` 4.3.4 e
`System.Text.RegularExpressions` 4.3.1: o `xunit` 2.4.2 (mesma versao do
monolito) arrasta essas duas via `NETStandard.Library` 1.6.1 em versoes com aviso
de vulnerabilidade High.
