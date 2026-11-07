using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Infrastructure.Observability;
using OficinaMecanica.Execucao.Infrastructure.Persistence;
using OficinaMecanica.Execucao.Integration.Tests.Support;

namespace OficinaMecanica.Execucao.Integration.Tests.Infra;

[Trait("Category", "Infra")]
[Collection(Colecoes.Infra)]
public class IndicesETelemetriaTests(InfraFixture infra)
{
    private static readonly DateTimeOffset Base = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    private static async Task<List<BsonDocument>> IndicesAsync(IMongoCollection<BsonDocument> colecao) =>
        await (await colecao.Indexes.ListAsync()).ToListAsync();

    [Fact(Timeout = 300_000)]
    public async Task Startup_DeveCriarOsIndicesDaFilaEPreCriarAsColecoesDoOutbox()
    {
        await using var ambiente = await AmbienteDeInfra.CriarAsync(infra);

        var indices = (await IndicesAsync(ambiente.Documentos)).ToDictionary(i => i["name"].AsString);

        indices.Keys.Should().Contain(["ix_status_enfileiradaEm", "ix_status_liberadaEm", "ix_mecanicoId_status", "ux_numero"]);
        indices["ix_status_enfileiradaEm"]["key"].AsBsonDocument.Names.Should().Equal("status", "enfileiradaEm");
        indices["ix_status_liberadaEm"]["key"].AsBsonDocument.Names.Should().Equal("status", "liberadaEm");
        indices["ix_mecanicoId_status"]["key"].AsBsonDocument.Names.Should().Equal("mecanicoId", "status");
        indices["ux_numero"]["unique"].AsBoolean.Should().BeTrue();
        indices["ux_numero"]["partialFilterExpression"].AsBsonDocument["_t"].AsString.Should().Be("ordem");

        var colecoes = await (await ambiente.Api.Banco.ListCollectionNamesAsync()).ToListAsync();
        colecoes.Should().Contain(MongoInitializer.ColecoesDoMassTransit);
        var inbox = (await IndicesAsync(ambiente.Api.Banco.GetCollection<BsonDocument>("inbox.states")))
            .Single(i => i["name"].AsString == "MessageId_1_ConsumerId_1");
        inbox["unique"].AsBoolean.Should().BeTrue();
    }

    [Fact(Timeout = 300_000)]
    public async Task MongoInitializer_ExecutadoDeNovo_DeveSerIdempotente()
    {
        await using var ambiente = await AmbienteDeInfra.CriarAsync(infra);
        var inicializador = ambiente.Api.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<MongoInitializer>().Single();

        var acao = async () =>
        {
            await inicializador.StartAsync(CancellationToken.None);
            await inicializador.StartAsync(CancellationToken.None);
            await inicializador.StopAsync(CancellationToken.None);
        };

        await acao.Should().NotThrowAsync();
        (await IndicesAsync(ambiente.Documentos)).Should().HaveCount(5);
    }

    [Fact(Timeout = 300_000)]
    public async Task IndiceUnicoDoNumero_NaoDeveAplicarseAoTombstone()
    {
        await using var ambiente = await AmbienteDeInfra.CriarAsync(infra);

        await ambiente.Documentos.InsertOneAsync(new BsonDocument { { "_id", new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard) }, { "_t", "cancelamentoPrevio" } });
        var acao = () => ambiente.Documentos.InsertOneAsync(new BsonDocument { { "_id", new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard) }, { "_t", "cancelamentoPrevio" } });

        await acao.Should().NotThrowAsync("dois tombstones sem numero nao colidem no indice parcial");
    }

    [Theory(Timeout = 300_000)]
    [InlineData("Fila", "enfileiradaEm", "ix_status_enfileiradaEm")]
    [InlineData("AguardandoExecucao", "liberadaEm", "ix_status_liberadaEm")]
    public async Task ConsultaDoClaim_DeveUsarOIndiceDaFilaSemOrdenarEmMemoria(string status, string campo, string indiceEsperado)
    {
        await using var ambiente = await AmbienteDeInfra.CriarAsync(infra);

        var plano = await ambiente.Api.Banco.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            {
                "explain", new BsonDocument
                {
                    { "find", "ordens_producao" },
                    { "filter", new BsonDocument { { "_t", "ordem" }, { "status", status } } },
                    { "sort", new BsonDocument(campo, 1) },
                    { "limit", 1 }
                }
            },
            { "verbosity", "queryPlanner" }
        });

        var json = plano.ToJson();
        json.Should().Contain("IXSCAN").And.Contain(indiceEsperado);
        json.Should().NotContain("\"stage\": \"SORT\"");
    }

    [Fact(Timeout = 300_000)]
    public async Task MongoIdadeDaFilaReader_DeveDevolverODesdeDaOrdemMaisAntigaDeCadaStatus()
    {
        await using var ambiente = await AmbienteDeInfra.CriarAsync(infra);
        var diagnosticoAntigo = new OrdemDeProducaoBuilder().EnfileiradaEm(Base.AddHours(-1)).DoMecanico("m1").EmDiagnostico();
        var naFila = new OrdemDeProducaoBuilder().EnfileiradaEm(Base.AddHours(-5)).NaFila();
        var naFilaMaisNova = new OrdemDeProducaoBuilder().EnfileiradaEm(Base.AddHours(-2)).NaFila();
        var aguardando = new OrdemDeProducaoBuilder().EnfileiradaEm(Base.AddHours(-8)).DiagnosticoConcluido();
        aguardando.LiberarParaExecucao(aguardando.Servicos, aguardando.Pecas, Base.AddHours(-3));
        var emExecucao = new OrdemDeProducaoBuilder().EnfileiradaEm(Base.AddHours(-9)).EmExecucao();
        await ambiente.SemearAsync(diagnosticoAntigo, naFila, naFilaMaisNova, aguardando, emExecucao);
        var leitor = ambiente.Api.Services.GetRequiredService<IIdadeDaFilaReader>();

        var desde = await leitor.ObterInicioDaMaisAntigaAsync(CancellationToken.None);

        desde[StatusProducao.Fila].Should().BeCloseTo(Base.AddHours(-5), TimeSpan.FromMilliseconds(1));
        desde[StatusProducao.AguardandoExecucao].Should().BeCloseTo(Base.AddHours(-3), TimeSpan.FromMilliseconds(1));
        desde[StatusProducao.EmDiagnostico].Should().BeCloseTo(Base.AddHours(-1).AddMinutes(1), TimeSpan.FromMilliseconds(1));
        desde[StatusProducao.EmExecucao].Should().BeCloseTo(Base.AddHours(-9).AddMinutes(4), TimeSpan.FromMilliseconds(1));
        desde.Should().NotContainKey(StatusProducao.Finalizada);
    }

    [Fact(Timeout = 300_000)]
    public async Task MetricasDaFila_DeveReportarTamanhoEIdadeAPartirDoBanco()
    {
        await using var ambiente = await AmbienteDeInfra.CriarAsync(infra);
        await ambiente.SemearAsync(
            new OrdemDeProducaoBuilder().EnfileiradaEm(DateTimeOffset.UtcNow.AddMinutes(-30)).NaFila(),
            new OrdemDeProducaoBuilder().NaFila(),
            new OrdemDeProducaoBuilder().Finalizada());
        using var captura = new CapturaDeMetricas();
        var servico = ambiente.Api.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .OfType<MetricasDaFilaBackgroundService>().Single();

        await servico.AtualizarAsync(CancellationToken.None);
        var leitura = captura.LerObservaveis();

        leitura.Single(m => m.Instrumento == "fila_ordens" && (string)m.Tags["status"]! == "Fila").Valor.Should().Be(2);
        leitura.Single(m => m.Instrumento == "fila_ordens" && (string)m.Tags["status"]! == "Finalizada").Valor.Should().Be(1);
        leitura.Single(m => m.Instrumento == "fila_idade_mais_antiga_segundos" && (string)m.Tags["status"]! == "Fila")
            .Valor.Should().BeGreaterThan(29 * 60);
    }

    [Fact(Timeout = 300_000)]
    public async Task Contadores_DevemSerPublicadosSoDepoisDoCommitEUmaVezPorEfeito()
    {
        await using var ambiente = await AmbienteDeInfra.CriarAsync(infra);
        using var captura = new CapturaDeMetricas();
        var ordemId = Guid.NewGuid();
        var cancelada = Guid.NewGuid();

        // Consumer: enfileirar (1), reentrega (no-op) e cancelamento previo de outra ordem (1).
        await ambiente.Os.EnviarAsync(new SolicitarDiagnosticoBuilder().ComOrdemDeServicoId(ordemId).Build());
        await ambiente.Os.EnviarAsync(new SolicitarDiagnosticoBuilder().ComOrdemDeServicoId(ordemId).Build());
        await ambiente.Os.EnviarAsync(new CancelarProducaoBuilder().ComOrdemDeServicoId(cancelada).Build());
        (await Esperar.AteAsync(() => captura.Total("ordens_enfileiradas_total") >= 1 && captura.Total("cancelamentos_previos_total") >= 1)).Should().BeTrue();

        // HTTP: claim, conclusao do diagnostico, e depois um inviabilizar apos liberar.
        var mecanico = ambiente.Cliente("mecanico-metricas");
        (await mecanico.PostAsync("/api/diagnosticos/proximo", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await mecanico.PostAsJsonAsync($"/api/ordens-de-producao/{ordemId}/diagnostico/concluir", new { laudo = "ok" })).EnsureSuccessStatusCode();
        await ambiente.Os.EnviarAsync(new IniciarExecucaoBuilder().ComOrdemDeServicoId(ordemId).Build());
        (await Esperar.AteAsync(async () => (await ambiente.ConsultarAsync(ordemId))!.Status == "AguardandoExecucao")).Should().BeTrue();
        (await mecanico.PostAsync("/api/reparos/proximo", null)).EnsureSuccessStatusCode();
        (await mecanico.PostAsJsonAsync($"/api/ordens-de-producao/{ordemId}/reparo/inviabilizar", new { motivo = "sem peca" })).EnsureSuccessStatusCode();
        await Task.Delay(500);

        captura.Total("ordens_enfileiradas_total").Should().Be(1);
        captura.Total("cancelamentos_previos_total").Should().Be(1);
        captura.Total("diagnosticos_concluidos_total").Should().Be(1);
        captura.Total("ordens_inviabilizadas_total").Should().Be(1);
        captura.Total("reparos_finalizados_total").Should().Be(0);
    }
}
