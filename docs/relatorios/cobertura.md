# Evidencia de cobertura de testes

Este documento registra a cobertura de linhas do `OficinaMecanica.Execucao`, como
ela e medida, o que fica fora da conta e por que, e como reproduzir o numero.
Os valores vieram da execucao local de `bash scripts/cobertura.sh` sobre o codigo
como esta hoje (inclusive os testes do abandono de requisicao pelo cliente e das
metricas HTTP) e estao registrados no `README.md` e no `CLAUDE.md`; nada aqui foi
medido pelo CI do GitHub. O numero do SonarCloud na `main` do repositorio publicado
(93,5%) esta na secao "SonarCloud".

## Medicao

| Campo | Valor |
|---|---|
| Data da medicao | 2026-11-08 |
| Comando | `bash scripts/cobertura.sh` (as mesmas fases de testes e relatorio que o job `Build & Test` do CI chama) |
| Escopo | Todos os projetos de teste, **incluindo os de infraestrutura** (Testcontainers: MongoDB 7.0.45 em replica set e RabbitMQ 4.4.0) |
| Ferramentas | coverlet (formato OpenCover) e uniao dos relatorios no ReportGenerator 5.5.12 |
| Meta | >= 80% de linhas do servico inteiro (Contracts, Domain, Application, Infrastructure e API) |
| Gate | Valido para o numero **com exclusoes**; abaixo de 80% o CI reprova |

### Resultado

| Medicao | Linhas cobertas | Linhas a cobrir | Percentual |
|---|---|---|---|
| **Bruta** (conta tudo) | 1899 | 1910 | **99,42%** |
| **Com exclusoes** (numero do gate) | 1718 | 1728 | **99,42%** |

A diferenca entre as duas e a soma das linhas dos arquivos excluidos
(`Program.cs` e `DependencyInjection*.cs`, ver abaixo): 182 linhas a cobrir,
das quais 181 cobertas e 1 nao coberta (calculado a partir dos dois numeros acima).
`Program.cs` entra **coberto** porque os testes de API sobem a aplicacao inteira.

### Por assembly (com exclusoes)

| Assembly | Linhas cobertas / a cobrir | Cobertura de linhas |
|---|---|---|
| `OficinaMecanica.Contracts` | 47 / 47 | 100% |
| `OficinaMecanica.Execucao.Domain` | 275 / 275 | 100% |
| `OficinaMecanica.Execucao.API` | 253 / 253 | 100% |
| `OficinaMecanica.Execucao.Application` | 487 / 488 | 99,7% |
| `OficinaMecanica.Execucao.Infrastructure` | 656 / 665 | 98,6% |

### Testes por projeto

| Projeto de teste | Testes |
|---|---|
| `OficinaMecanica.Contracts.Tests` | 23 |
| `OficinaMecanica.Execucao.Domain.Tests` | 185 |
| `OficinaMecanica.Execucao.Application.Tests` | 162 |
| `OficinaMecanica.Execucao.Integration.Tests` | 291 (238 sem Docker e 53 `Category=Infra`) |
| **Total** | **661** |

## O que fica de fora da conta, e por que

Nenhuma exclusao e necessaria para atingir a meta: sem elas o numero bruto
(99,42%) tambem fica muito acima de 80%. Elas existem para o numero do gate nao
depender de codigo de inicializacao.

| Exclusao | Onde se aplica | Motivo |
|---|---|---|
| `Program.cs` | Somente no relatorio "com exclusoes" e no SonarCloud (`sonar.coverage.exclusions`) | Bootstrap da aplicacao (composicao e pipeline HTTP), sem regra de negocio |
| `DependencyInjection*.cs` | Idem | Registro de servicos na injecao de dependencia, bootstrap |
| Codigo marcado com `GeneratedCode` ou `ExcludeFromCodeCoverage` | Na **coleta** (`coverage.runsettings`), logo nas duas medicoes | Codigo gerado por ferramenta nao e escrito por nos. `[ExcludeFromCodeCoverage]` e **proibido sem um ADR**: o codigo marcado some em silencio do numerador e do denominador, e o gate nao tem como julgar a justificativa. O `scripts/cobertura.sh` lista as ocorrencias em `src/` no relatorio e no Job Summary; hoje ha **nenhuma** |

**`CompilerGeneratedAttribute` nao e excluido, de proposito.** Exclui-lo tiraria da
medida iteradores, expressoes lambda e maquinas de estado de `async`/`await`, que
sao codigo escrito por nos (compilado para classes geradas) e inflaria o numero.

A mesma lista de arquivos excluidos alimenta o SonarCloud, para o gate do
repositorio e o do Sonar medirem a mesma coisa.

## Como o gate funciona

- `scripts/verificar-cobertura.sh <Summary.txt> [minimo]` calcula o percentual a
  partir dos contadores **inteiros** `Covered lines` e `Coverable lines`, e nao da
  linha `Line coverage`, que o ReportGenerator arredonda (79,96% apareceria como
  80% e passaria). Falha (saida diferente de zero) abaixo do minimo.
- O limite e `COBERTURA_MINIMA` (padrao 80; o `ci.yml` exporta `"80"`).
- O gate e testado: `scripts/testar-scripts.sh` verifica que 79% reprova e 81%
  aprova, que `Program.cs` e `DependencyInjection.cs` ficam fora do numero do gate,
  que cada filtro de exclusao tem um mutante que o detecta e que apagar a chamada do
  gate dentro do `cobertura.sh` e detectado (provando que o gate esta ligado ao
  relatorio).

## Como regenerar

Pre-requisitos: .NET 8 SDK, `bash` e, para os testes de infraestrutura, Docker ligado.

```bash
bash scripts/cobertura.sh          # roda cada projeto de teste uma vez, une a cobertura e aplica o gate
bash scripts/ci-local.sh           # o pipeline completo de Build & Test (da ordem de 7 minutos com Docker)
```

O `cobertura.sh` aceita uma fase (`testes-sem-infra`, `testes-infra` ou
`relatorio`), que e como o CI o chama. Com `ci-local.sh --sem-infra` (sem Docker) a
cobertura completa nao e avaliada e o gate nao vale.

Saidas, todas em `TestResults/` (pasta ignorada pelo Git, entao o HTML **nao** e
versionado: para ve-lo, gere de novo):

| Arquivo | Conteudo |
|---|---|
| `TestResults/report-com-exclusoes/index.html` | Relatorio HTML do numero do gate (abra no navegador) |
| `TestResults/report-bruto/index.html` | Relatorio HTML do numero bruto |
| `TestResults/report-com-exclusoes/Summary.txt` e `TestResults/report-bruto/Summary.txt` | Resumo em texto, de onde o gate le os contadores |
| `TestResults/resumo-cobertura.md` | Resumo em Markdown (o mesmo que o CI copia para o Job Summary) |

## Onde o relatorio aparece no CI

Desde a publicacao (2026-11-09/10) o workflow roda no GitHub. O job
**Build & Test** publica estes artefatos de cada execucao (definidos no `ci.yml`):

- `relatorio-de-cobertura`: os dois relatorios HTML (`report-com-exclusoes` e
  `report-bruto`);
- `cobertura-opencover`: os arquivos OpenCover (7 dias), que o job `Quality`
  importa no SonarCloud em vez de rodar os testes de novo;
- `resultados-dos-testes`: os `.trx`.

Alem disso, o resumo de cobertura e escrito no **Job Summary** da execucao.

## SonarCloud

O job `Quality` roda desde a publicacao (2026-11-09/10) e o Quality Gate foi aprovado nos
pipelines de `main`. A cobertura medida pelo SonarCloud na `main` e **93,5%**; o script local
mede 99,42% (2026-11-08). Os dois numeros vem de ferramentas diferentes e nao foram
reconciliados neste documento.

| Item | Valor |
|---|---|
| Badge | (a preencher com o badge do projeto no SonarCloud) |
| Link do projeto no SonarCloud | (a preencher com a URL do projeto no SonarCloud) |
| Quality Gate da ultima execucao em `main` | Aprovado (job `Quality` verde) |
| Cobertura de linhas no Sonar (`main`) | 93,5% |

## Limites desta evidencia

- Cobertura de **linhas** nao prova qualidade dos testes: ela mostra o que foi
  executado, nao o que foi verificado. O repositorio complementa com testes de falha
  e de mutacao dos scripts de gate, com testes de infraestrutura contra MongoDB e
  RabbitMQ reais e com o Quality Gate do SonarCloud.
- Os numeros das tabelas acima sao de uma execucao local em 2026-11-08, em Windows com
  Docker. A medicao em ambiente limpo (runner do GitHub) e a que o CI e o SonarCloud
  produzem a cada execucao; o unico numero do runner registrado aqui e o do Sonar na
  `main` (93,5%).
