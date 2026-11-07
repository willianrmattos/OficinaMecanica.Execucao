# ADR 0001: MongoDB Atlas para a fila de execucao

## Status
Aceito (comportamento no Atlas M0 ainda nao verificado: ver "Consequencias")

## Contexto
O servico de Execucao guarda a producao da oficina: a fila de ordens a diagnosticar, o diagnostico em
andamento (com apontamentos e laudo), a fila de reparo e o resultado. Cada ordem e lida e alterada quase
sempre sozinha (reivindicar, apontar, concluir, finalizar), carrega listas pequenas embutidas (servicos,
pecas, etapas, apontamentos) e nao tem juncao com dado de outro servico. A regra de independencia do
ecossistema exige banco proprio por servico, e o projeto exige pelo menos um banco NoSQL entre os
servicos. O Atlas tem camada gratuita (M0), compativel com o orcamento do projeto.

## Decisao
**MongoDB** (Atlas M0 em producao; Mongo 7 em replica set de 1 no no compose e nos testes), com **um
documento por ordem de servico** na colecao `ordens_producao`:

- `_id` = `OrdemDeServicoId`. O enfileiramento repetido da mesma mensagem cai na mesma chave, e o E11000 e a
  idempotencia natural; nao ha tabela de deduplicacao propria alem do inbox do MassTransit.
- Servicos, pecas, etapas, apontamentos e laudo ficam **embutidos** no documento; a ordem e a unidade de
  consistencia e de leitura. Campos em camelCase, enums como texto, datas como data BSON (UTC, precisao de
  milissegundo) e `version` (long) para concorrencia otimista. `GuidRepresentation.Standard` e registrado
  antes do primeiro uso do driver. O mapeamento e feito por `BsonClassMap` na Infrastructure, sem nenhuma
  referencia ao driver no Domain.
- **Claim da fila sem lock distribuido**: le-se a primeira ordem do status (FIFO por `enfileiradaEm` na fila
  de diagnostico e por `liberadaEm` na de reparo) e grava-se com filtro `{_id, version}`. Quem perde recebe
  `ConcorrenciaException` e le a proxima; dois mecanicos nunca ficam com a mesma ordem. A leitura da fila e
  feita fora da transacao (sempre enxerga o estado commitado) e a escrita do claim entra na transacao do
  outbox quando ha evento a publicar (ver ADR 0002).
- Indices criados no startup, de forma idempotente: `{status, enfileiradaEm}`, `{status, liberadaEm}`,
  `{mecanicoId, status}` e `{numero}` unico **parcial** (so documentos de ordem). Os planos de execucao das
  consultas do claim usam o indice, sem ordenacao em memoria (testado com `explain`).
- **Tombstone de cancelamento previo na MESMA colecao e com a MESMA chave** (`_id` =
  `OrdemDeServicoId`), separado por um discriminador `_t` (`ordem` ou `cancelamentoPrevio`) que todas as
  leituras de ordem, filas, listagens e contagens filtram. A saga manda `SolicitarDiagnostico` e depois
  `CancelarProducao` ao mesmo endpoint, entao as duas mensagens podem rodar em paralelo; com documentos em
  chaves diferentes, a transacao (snapshot) nao impede o write skew e sobraria uma ordem orfa na `Fila`.
  Na mesma chave, as duas escritas colidem: a que perde aborta (ou recebe E11000), o retry reexecuta o
  handler e a pre-checagem ja existente vira no-op ou cancela a ordem. Fora de transacao, a colisao do `_id`
  devolve `false` so quando o ocupante e do mesmo tipo (ou quando e tombstone ocupando o lugar da ordem);
  tombstone que encontra uma ordem lanca `ConcorrenciaException`, para o cancelamento nunca ser descartado.
- Replica set e obrigatorio: o outbox transacional do MassTransit.MongoDb usa transacoes.

## Consequencias
### Positivas
- Uma leitura e uma escrita por operacao de negocio, sem juncoes; o documento reflete o agregado do Domain.
- Idempotencia e anti-corrida vem de uma chave unica, nao de lock; o teste de corrida (varias rodadas, com
  encontro forcado dos dois handlers) prova que nunca sobra ordem na `Fila` e falha se tombstone e ordem
  forem separados em colecoes diferentes.
- O driver e o MassTransit 8.5.12 sao os mesmos que o spike validou; a camada de dominio nao conhece Mongo.

### Negativas
- Atlas M0 (documentacao oficial): 500 conexoes, ~100 operacoes por segundo (estrangula em vez de
  rejeitar), 0,5 GB. O outbox do MassTransit custa ~37 operacoes por mensagem (medido no spike), o que
  limita o teto a poucas mensagens por segundo; o intervalo de leitura do outbox e configuravel
  (`Mensageria:IntervaloDoOutboxEmMs`, padrao 1 s).
- **Pendente**: teste real de transacao com consumer no Atlas M0 (exige credenciais do usuario). Se o M0
  rejeitar ou estrangular transacoes, o fallback do spike (outbox embutido no documento + relay) fica
  disponivel, ao custo de codigo proprio e de entrega duplicada sem lease.
- A data BSON tem precisao de milissegundo: `EnfileiradaEm`/`LiberadaEm` empatados no mesmo milissegundo
  ficam em ordem arbitraria entre si.
- Reidratar o agregado exige campos privados nao `readonly` (`_etapas`, `_apontamentos`), unica alteracao
  no Domain por causa da persistencia.
- Tombstones nao expiram: ha um por cancelamento previo, e so o enfileiramento tardio os consulta. Se o volume
  crescer, um indice TTL em `registradoEm` (filtro parcial por `_t`) resolve sem mudar o contrato.
- Dados de outro servico nao podem ser consultados por juncao: a Execucao so conhece o que a saga envia.

## Alternativas consideradas
- **Azure SQL (como OS Service e Billing)**: serviria (claim com `UPDLOCK, READPAST`), mas o projeto exige um
  banco NoSQL e o modelo de documento por ordem encaixa sem tabelas auxiliares.
- **Cosmos DB (API do MongoDB)**: gerenciado no Azure, mas sem camada gratuita equivalente e com semantica
  de transacao propria; adiciona custo e acoplamento ao provedor.
- **Ordem e tombstone em colecoes separadas, serializados por lock**: o lock distribuido por
  `OrdemDeServicoId` seria codigo e falha novos; a chave unica compartilhada resolve a mesma corrida sem eles.
- **Fila como lista de mensagens no RabbitMQ**: a ordem precisa de consulta, listagem, apontamentos e
  estado duradouro; o broker nao e armazenamento de consulta.
