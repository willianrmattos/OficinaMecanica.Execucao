# ADR 0003: CI/CD independente com gate de qualidade

## Status
Aceito

Atualizacao em 2026-11-10: o pipeline foi publicado (PRs #1 a #3 mergeados em `release` e em `main`, 2026-11-09/10) e os cinco
jobs ficaram verdes em `main`: Build & Test, Quality (SonarCloud), Scan Image (Trivy), Build & Push no ACR e Deploy no AKS. O
Contexto abaixo descreve o momento da decisao, antes da publicacao, e nao foi reescrito.

## Contexto
A Execucao e um microsservico independente: repositorio, banco (MongoDB Atlas, banco `execucao`), credencial de banco
dedicada, identidade OIDC propria e rota propria na APIM. Ela precisa de um pipeline proprio que prove, a cada PR, que o
codigo compila, que os testes (inclusive os de infraestrutura com MongoDB em replica set e RabbitMQ reais) passam, que a
cobertura de linhas e de pelo menos 80% e que a qualidade estatica esta aprovada, e que depois publique a imagem e
implante no AKS sem segredo de longa duracao. O pipeline segue o molde do Billing (ADR 0003 do `OficinaMecanica.Billing`);
as partes especificas do servico ficam isoladas em poucos pontos.

Restricoes que moldam as escolhas: o MassTransit 9.x e comercial (o ecossistema fica no 8.x); o cluster tem um unico no
sem CPU de requests sobrando; o MongoDB nao tem esquema nem migration (os indices e as colecoes sao criados pela propria
API na partida); com o broker fora o MassTransit (`WaitUntilStarted`) impede a API de abrir a porta, sem limite de tempo;
o repositorio ainda nao existe no GitHub, entao tudo foi validado localmente (`actionlint`, `kubeconform`, `docker build`,
execucao da imagem contra MongoDB e RabbitMQ reais e execucao dos scripts) e a primeira execucao real do workflow
acontece na publicacao.

## Decisao
**Cinco jobs encadeados em `.github/workflows/ci.yml`:**
- `build-and-test` (`Build & Test`): restore, build Release, testes dos scripts de CI, guardrail do MassTransit, validacao dos
  manifests, conferencia do OpenAPI, testes `Category!=Infra`, testes `Category=Infra` (Testcontainers; o runner ubuntu ja
  traz Docker), cobertura mesclada com gate e publicacao dos resultados (`dorny/test-reporter`, Job Summary e artifacts).
- `quality` (`Quality`): SonarCloud, depende do job anterior.
- `scan-image`: so `push`/`workflow_dispatch` em `main` e `release`; constroi a imagem, escaneia com o Trivy e a guarda como
  artifact. **So tem `contents: read`**.
- `build-and-push-image`: mesma condicao; baixa o artifact, carrega a MESMA imagem e a empurra para o ACR depois do login OIDC.
  PR nunca publica imagem.
- `deploy-to-aks`: mesma condicao; `kubectl apply`, `kubectl set image`, `rollout status`, com `concurrency` propria. **Sem
  migration**: nao ha `dotnet ef` nem leitura de secret no deploy; os indices (`ix_status_enfileiradaEm`,
  `ix_status_liberadaEm`, `ix_mecanicoId_status`, `ux_numero`) e as colecoes do outbox/inbox sao criados de forma idempotente
  pelo `MongoInitializer` na partida de cada pod, antes de o bus consumir.

**Gate de cobertura de 80% sobre o numero mesclado.** `scripts/cobertura.sh` coleta OpenCover (coverlet) e `.trx` em cada
projeto de teste na mesma execucao (os testes rodam uma unica vez) e une tudo com o ReportGenerator, inclusive os testes
de infraestrutura. O gate (`scripts/verificar-cobertura.sh`) vale para o numero "com exclusoes", que tira apenas o que o
`CLAUDE.md` documenta (`Program.cs` e `DependencyInjection*`); o numero bruto e publicado ao lado. O percentual sai dos
contadores inteiros `Covered lines`/`Coverable lines` (o mesmo dado de `lines-covered`/`lines-valid` do Cobertura), e nao da
linha `Line coverage`, que o ReportGenerator arredonda: 79,96% apareceria como 80% e passaria. O limite e `COBERTURA_MINIMA`
(padrao 80; vazio conta como nao definido) e o script falha com saida diferente de zero abaixo dele. A mesma lista de
exclusoes alimenta o Sonar (`sonar.coverage.exclusions`). Cada `dotnet test` do `cobertura.sh` roda com
`--blame-hang-timeout 5m --blame-hang-dump-type none`: um teste que espera uma tarefa que o codigo sob teste deixou de liberar
penduraria o run em silencio (so o `timeout-minutes` do job o encerraria, e o `ci-local.sh` nao tem timeout); com o limite o
testhost e encerrado e o teste aparece como falha.

**`[ExcludeFromCodeCoverage]` e proibido sem ADR.** O coverlet o honra (`coverage.runsettings`, junto de `GeneratedCode`;
`CompilerGenerated` nao e excluido, porque tiraria iteradores, lambdas e maquinas de estado async e inflaria a medida): o codigo marcado some em silencio das duas medicoes, nem entra
no denominador, e o gate nao tem como julgar a justificativa. `scripts/cobertura.sh` lista as ocorrencias de
`ExcludeFromCodeCoverage` em `src/` no relatorio e no Job Summary; hoje nao ha nenhuma.

**Os proprios gates sao testados.** `scripts/testar-scripts.sh` (step do `Build & Test` e do `ci-local.sh`) exercita o caminho
de FALHA de todos os scripts de CI com entradas sinteticas (70 casos) e, depois, planta defeitos em copias dos scripts
exigindo que cada um seja detectado (31 mutantes; dentro de um mutante o primeiro caso que reprova basta). Os casos:
- **gate e guardrail isolados**: 79,9% reprova, 80,0% aprova, 79,96% arredondado reprova, resumo ausente, `COBERTURA_MINIMA`
  vazio, MassTransit 9.x direto e so transitivo, `MassTransit.MongoDb`, saida do Windows com CRLF. O guardrail ganhou o modo
  `--entrada <arquivo>` para analisar uma saida sintetica sem restore;
- **`verificar-workflow.sh`** (separacao de privilegios do `ci.yml`: job com `id-token` so executa `actions/*` e `azure/*`);
- **valor do gate no CI**: o `ci.yml` exporta `COBERTURA_MINIMA: "80"` (um mutante troca o 80 por 10 numa copia do `ci.yml`: reduzir o
  gate no workflow nao pode passar despercebido);
- **`cobertura.sh relatorio` de ponta a ponta**, sobre um OpenCover sintetico montado em diretorio temporario com o ReportGenerator
  fixado: 79% reprova, 81% aprova, `Program.cs` e `DependencyInjection.cs` sem cobertura ficam fora do numero do gate (so o "com
  exclusoes" vale; cada filtro de exclusao tem um mutante proprio), o aviso de `[ExcludeFromCodeCoverage]` lista os arquivos de
  `src/` e conta as ocorrencias no resumo, `COBERTURA_MINIMA`, fase invalida e ausencia de relatorios (exit 2). E isto que prova que o gate esta ligado ao relatorio: sem
  esses casos, apagar a linha `bash scripts/verificar-cobertura.sh ...` do `cobertura.sh` deixaria o CI aprovando qualquer
  cobertura e o `testar-scripts.sh` continuaria verde (um mutante agora apaga a linha e exige que um caso reprove);
- **`validar-k8s.sh`** (diretorio inexistente nao aprova; Deployment valido e CRD sem schema aprovam; campo desconhecido
  reprova por causa do `-strict`; usa o kubeconform em Docker e e pulado, com aviso, sem Docker);
- **`exportar-swagger.sh --verificar`** com um `dotnet` de mentira no `PATH` (grava um documento sintetico no lugar do OpenAPI da
  API): em dia aprova (inclusive com CRLF e `\r\n` dentro das strings, que o script normaliza), divergente reprova, sem
  `docs/openapi.json` reprova, sem `--verificar` regrava o arquivo normalizado; argumento desconhecido sai 2 (tambem no
  `ci-local.sh`);
- **ligacao `ci.yml` x `ci-local.sh`**: os dois chamam os mesmos passos (incluindo `cobertura.sh relatorio`, o do gate).
Os scripts que escrevem rodam sempre numa copia temporaria (sandbox), nunca em `TestResults/`, `artifacts/` ou `docs/` do
repositorio.

**SonarCloud com Quality Gate bloqueante.** O job `quality` baixa o OpenCover do job anterior (artifact), roda
`dotnet-sonarscanner begin/build/end` e usa `sonar.qualitygate.wait=true`: se o Quality Gate reprova, o job falha. Projeto e
organizacao vem de variaveis do repositorio (`vars.SONAR_PROJECT_KEY`, `vars.SONAR_ORGANIZATION`) e o `SONAR_TOKEN` de
`secrets`; e o unico secret de longa duracao do pipeline, porque o SonarCloud nao suporta OIDC. Sem token o job falha com
mensagem clara; em PR de fork (sem acesso a secrets) o job e pulado.

**Trivy antes do push, em job sem `id-token`.** As actions que constroem e escaneam a imagem (`docker/*`,
`aquasecurity/trivy-action`) sao de terceiros e, mesmo fixadas por SHA (decisao abaixo), o codigo delas nao foi auditado. Num job com `id-token: write` qualquer
step pode pedir um token OIDC com `AcrPush` e `AKS Cluster Admin`; por isso o `scan-image` so le o repositorio e o job de push
(que tem `id-token: write`) so executa `actions/download-artifact`, `azure/login` e comandos `docker`. O scan reprova
`HIGH`/`CRITICAL` com correcao disponivel (`ignore-unfixed`); a imagem escaneada viaja como artifact (`docker save`) e o job de
push a carrega (`docker load`) e empurra a MESMA imagem, sem rebuild. A tag de deploy e sempre o sha do commit; `:latest` so
sai da `main`, para um push em `release` nao sobrescrever o que esta em producao.

**Actions por SHA (aplicado em 2026-11-09).** Todas as actions do `ci.yml`, de terceiros (`docker/setup-buildx-action`,
`docker/build-push-action`, `aquasecurity/trivy-action`, `dorny/test-reporter`) e do GitHub/Microsoft (`actions/*`, `azure/login`,
`azure/aks-set-context`), estao fixadas pelo SHA completo do commit, com a versao num comentario ao lado
(`uses: actions/checkout@<sha> # v4.4.0`). Uma tag e mutavel: quem controla a action pode move-la para outro codigo; o SHA nao.
O SHA veio da tag que o workflow ja usava, resolvida pela API do GitHub para o commit em que ela apontava no dia, e a versao do
comentario e a tag semver mais especifica que aponta para o mesmo commit (no `dorny/test-reporter` e `v1`). O pin nao audita o
conteudo: impede que a tag seja movida depois, mas nao prova que o commit de hoje e confiavel. O Trivy estava referenciado como
`0.28.0`, tag que nao existe no repositorio da action (a real e `v0.28.0`); o pin aponta hoje para a `v0.36.0`: a `v0.28.0` chama internamente `setup-trivy@v0.2.1`, tag que deixou de existir, e a `v0.36.0` fixa as actions internas por SHA. O Dependabot
(`github-actions`) propoe as atualizacoes mantendo o formato `@sha # versao`.

**Guardrail do MassTransit.** `scripts/verificar-masstransit.sh` roda `dotnet list package --include-transitive` e falha se
qualquer pacote `MassTransit*` (`MassTransit`, `MassTransit.RabbitMQ`, `MassTransit.MongoDb`, `MassTransit.Abstractions`),
direto ou transitivo, resolver para major diferente de 8; se o proprio `dotnet list package` falhar, o script tambem falha
(nunca aprova sem enxergar as versoes). O Dependabot tambem ignora o major do `MassTransit*` e o do `MongoDB.Driver*` (que
fica na versao que o `MassTransit.MongoDb 8.5.12` traz).

**OIDC.** `azure/login` com a identidade da Execucao (`github_oidc_aks_app` no `OficinaMecanica.Infra`: so `push` em
`main`/`release`, sem credencial para `pull_request`; `AcrPush` e `AKS Cluster Admin`, e nenhuma role no Key Vault: o
`key_vault_secret_scopes` dela e vazio). Os jobs que falam com o Azure declaram `id-token: write`; os demais ficam com
`contents: read`. Os checkouts usam `persist-credentials: false` (nenhum job precisa do token do repositorio depois do
checkout) e o job de push nem faz checkout. Como nao ha migration, o pipeline nao le segredo: a
connection string so chega ao pod pelo CSI, com a identidade do addon do AKS. O deploy tem `concurrency: deploy-aks-execucao` (`cancel-in-progress: false`):
`main` e `release` apontam para o MESMO Deployment e a concurrency do workflow e por ref, entao sem o grupo dois deploys
poderiam rodar rollouts ao mesmo tempo; um deploy ainda pendente e substituido pelo mais novo. O render dos manifests falha se
`IMAGE` vier vazia (`${IMAGE:?}`) antes de qualquer chamada ao cluster.

**OpenAPI versionado.** `scripts/exportar-swagger.sh` gera o documento com `swagger tofile` (ferramenta fixada em
`dotnet-tools.json`) e `--verificar` falha se `docs/openapi.json` difere da API compilada. O `swagger tofile` executa o
`Program.Main` e espera o host subir, e a partida da Execucao cria os indices no MongoDB: o script sobe um `mongod`
descartavel em Docker (ou usa `SWAGGER_MONGO_URI`) e roda a API em `Testing` com transporte em memoria e sem outbox. O
arquivo e normalizado para LF, inclusive o `\r\n` que o compilador deixa dentro das strings dos comentarios XML no Windows,
para o resultado nao depender de onde foi gerado.

**Ruleset versionado e `Quality` obrigatorio.** `.github/rulesets/protect-main-release.json` replica a protecao dos demais
repositorios (`main` e `release`: sem delete nem force-push, PR obrigatorio) e exige os checks `Build & Test` e `Quality`;
nao ha script para aplica-lo: o JSON e a entrada direta da GitHub CLI (`gh api --method POST
repos/willianrmattos/OficinaMecanica.Execucao/rulesets --input .github/rulesets/protect-main-release.json`), executada manualmente
na publicacao, depois do primeiro CI. O `testar-scripts.sh` confere que os contextos exigidos pelo ruleset sao o `name` de jobs
do `ci.yml`.

**Testes de infraestrutura no mesmo job.** Os testes `Category=Infra` rodam como steps do `Build & Test`, nao num job
`Infra Tests` proprio. Desvio consciente do desenho "job separado" do plano: a cobertura mesclada e o gate precisam dos
OpenCover dos dois grupos no mesmo workspace, e um job separado exigiria trafegar os OpenCover por artifact e manter um terceiro
nome de check; mantendo tudo em `Build & Test` os nomes dos checks obrigatorios continuam os mesmos em todos os repositorios.
Os dois grupos rodam em steps separados para aparecerem em separado no log. A evolucao natural, se o tempo do job incomodar, e
extrair `Infra Tests` publicando o OpenCover como artifact para o `Build & Test` fundir.

**Manifests.** `k8s/execucao/` com um unico placeholder, `${IMAGE}`, renderizado por `envsubst` com a imagem do sha antes do
`kubectl apply` (o Deployment nasce com a tag certa e nenhum apply regride a imagem); segredos (connection string do Atlas e
credenciais do RabbitMQ) so por SecretProviderClass/Key Vault; Ingress por prefixo `/execucao` com rewrite;
`strategy.maxSurge: 0` por causa do no unico. O `startupProbe` em `/health` aceita ate 5 minutos (60 x 5 s) porque, com o
broker fora, a API nao abre a porta; passado o prazo o pod reinicia e entra em `CrashLoopBackOff` ate o broker voltar (risco
aceito, ver `CLAUDE.md`). A readiness (`/health/ready`) usa `timeoutSeconds: 5`: os checks de banco e broker tem limite de 3 s,
valido inclusive com o broker congelado (o `RabbitMqHealthCheck` usa `WaitAsync` porque o cliente AMQP leva ~8 s para desistir
sozinho; ha teste com um listener TCP que nunca responde). O resultado da prontidao fica em cache de 5 s
(`Health__ProntidaoCacheSegundos`, menor que o periodo: cada probe ve uma medicao nova) porque o endpoint e anonimo e a APIM o
repassa. O `periodSeconds: 30` reduz so a carga das probes: nao evita trafego que o servico ja gera (o outbox consulta o MongoDB
a cada `IntervaloDoOutboxEmMs`) e custa ate 90 s para tirar um pod da rotacao.

## Consequencias
### Positivas
- Nenhum merge em `main`/`release` sem build, testes de infraestrutura reais, cobertura >= 80% e Quality Gate aprovado.
- O que o Trivy aprova e exatamente o que vai para o ACR; nenhuma imagem com vulnerabilidade corrigivel e publicada, e o codigo
  de terceiros que a constroi nao tem acesso a identidade do Azure.
- O gate de cobertura existe no repositorio (script) e no Sonar: uma falha de configuracao do SaaS nao deixa o limite sem
  guarda; e os scripts de gate tem testes de falha e de mutacao.
- Nenhum segredo de longa duracao do Azure; o pipeline de um servico nao consegue agir com a identidade de outro.
- `scripts/ci-local.sh` reproduz o job `Build & Test` na maquina do desenvolvedor, com os mesmos scripts.
- O contrato publico fica versionado (`docs/openapi.json`) e muda junto com o PR que altera a API.
- Sem migration, o deploy e so imagem: nao ha etapa que dependa do banco estar acordado, e nenhuma credencial de banco passa
  pelo runner.

### Negativas
- Testes de infraestrutura baixam imagens (MongoDB e RabbitMQ) e tornam o `Build & Test` mais lento; o job `quality`
  compila de novo porque o scanner precisa instrumentar o build.
- A conferencia do OpenAPI precisa de Docker (para o `mongod` temporario); sem Docker o script exige `SWAGGER_MONGO_URI`.
- O check `Quality` pulado (PR de fork) conta como satisfeito para o GitHub: PR de fork nao passa pelo Sonar e depende de
  revisao.
- Os indices sao criados pela API na partida: uma mudanca de indice incompativel com um existente (mesmo nome, outras chaves)
  derruba a partida do pod novo e exige um passo manual no Atlas; indice novo e idempotente e entra sem intervencao.
- Com uma replica e `maxSurge: 0` ha uma janela sem pod a cada deploy (ate o `startupProbe` passar); os comandos da saga
  esperam no RabbitMQ e o mecanico so perde a API durante a janela.
- Com o broker fora na partida o pod nao fica pronto e acaba em `CrashLoopBackOff` ate o RabbitMQ voltar; o `rollout status`
  do deploy (420 s) reprova nesse caso.
- O artifact da imagem (cerca de 100 MB, 1 dia de retencao) e o `docker load` somam um minuto ao pipeline de publicacao.
- **`/health/ready` anonimo exposto pela APIM**: a rota `execucaoserver` repassa qualquer path (wildcard), entao qualquer cliente
  da internet pode chamar `/execucaoserver/health/ready`. Sem protecao, cada chamada abriria uma conexao AMQP e faria um ping no
  Atlas (3 s de limite cada), um vetor de consumo de recursos (nao vaza dado: o corpo nao traz excecao). Mitigacao aplicada no
  proprio servico: o resultado dos checks `mongodb` e `rabbitmq` fica em cache de 5 s (`HealthCheckComCache`,
  `Health__ProntidaoCacheSegundos`; 0 desliga), com as chamadas simultaneas esperando uma unica avaliacao; o custo cai para no
  maximo uma conexao AMQP e um ping por janela, qualquer que seja a taxa de requisicoes. O cache e menor que o `periodSeconds` da
  readiness (30 s), entao cada probe ve uma medicao nova; excecao e cancelamento do chamador nunca sao guardados (os dois checks
  deixam o cancelamento subir; um 503 falso de uma requisicao abortada nao pode ficar 5 s no cache). Mitigacao no
  `OficinaMecanica.Infra` (ADR 0007 de la): a policy da APIM que devolve 404 para `/health/ready` e `/metrics` na rota publica
  esta escrita no Terraform do Infra, mas em 2026-11-10 `/health/ready` e `/metrics` ainda respondem 200 pela APIM, em todos
  os servicos: o bloqueio nao esta efetivo (pendente, so com autorizacao do usuario). As probes do kubelet e a coleta de
  metricas nao passam pela APIM. O cache limita o custo, mas nao esconde o endpoint.
- O runner do GitHub nao precisa alcancar o Atlas (o deploy nao toca o banco), mas o egress do AKS precisa estar na
  allowlist do Atlas (`OficinaMecanica.Banco`).
- Antes da publicacao o workflow so foi validado estaticamente e pelos scripts, e as actions estao fixadas por SHA sem que o
  conteudo de cada commit fixado tenha sido auditado (ver a decisao "Actions por SHA").

## Alternativas consideradas
- **Pipeline unico para todos os servicos**: acopla os repositorios e contraria o requisito de CI/CD independente.
- **Rodar os testes duas vezes** (um step de testes e outro para cobertura): dobra o tempo, principalmente com os testes de
  infraestrutura; a coleta junto do teste entrega o mesmo numero.
- **Gate de cobertura so no SonarCloud**: depende de configuracao externa; o script no repositorio mantem o limite
  verificavel localmente e no CI.
- **Gate lendo a linha `Line coverage` arredondada**: 79,96% passaria como 80%.
- **Scan e push no mesmo job** (o desenho inicial do molde): as actions de terceiros ficariam num job com `id-token: write`.
- **Trivy depois do push**: a imagem vulneravel ja estaria no registry quando o scan reprovasse.
- **Reconstruir a imagem no job de push** (segundo `build-push-action`): o cache costuma reproduzir as camadas, mas nao ha
  garantia de que sejam identicas as escaneadas.
- **Job de migration/criacao de indices no deploy**: exigiria a connection string do Atlas no runner; a criacao idempotente na
  partida ja e coberta por testes de infraestrutura (`IndicesETelemetriaTests`) e nao depende de uma etapa a mais.
- **Exportar o OpenAPI sem subir o host** (`SwaggerHostFactory` no assembly da API): duplicaria a composicao do `Program.cs`
  so para a ferramenta; subir um `mongod` descartavel mantem o documento igual ao da API real.
- **Manifest com `:latest` mais `kubectl set image`**: cada apply voltaria o Deployment para `latest` e o `set image`
  disparava um segundo rollout, com duas indisponibilidades seguidas no no unico.
- **`startupProbe` sem limite pratico** (por exemplo, 1 hora): esconderia uma API travada; os 5 minutos cobrem o broker
  reiniciando junto com o no e a falha persistente aparece como `CrashLoopBackOff`.
- **Service principal com client secret em vez de OIDC**: segredo de longa duracao para rotacionar e vazar.
