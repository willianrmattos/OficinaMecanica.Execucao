using FluentAssertions.Equivalency;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using OficinaMecanica.Execucao.Domain.Entities;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Infrastructure.Persistence;

namespace OficinaMecanica.Execucao.Integration.Tests.Persistence;

public class MongoMappingsTests
{
    public MongoMappingsTests()
    {
        MongoMappings.Registrar();
    }

    // O BSON guarda data com precisao de milissegundo e em UTC: comparo o instante, nao os ticks.
    private static EquivalencyAssertionOptions<T> ComDatasEmMilissegundos<T>(EquivalencyAssertionOptions<T> opcoes) =>
        opcoes
            .Using<DateTimeOffset>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, TimeSpan.FromMilliseconds(1)))
            .WhenTypeIs<DateTimeOffset>();

    private static OrdemDeProducao IdaEVolta(OrdemDeProducao ordem) =>
        BsonSerializer.Deserialize<OrdemDeProducao>(ordem.ToBsonDocument());

    public static IEnumerable<object[]> TodosOsStatus() =>
        Enum.GetValues<StatusProducao>().Select(status => new object[] { status });

    [Theory]
    [MemberData(nameof(TodosOsStatus))]
    public void OrdemEmQualquerStatus_DeveFazerIdaEVoltaSemPerderCampos(StatusProducao status)
    {
        var ordem = new OrdemDeProducaoBuilder().NoStatus(status);

        var reidratada = IdaEVolta(ordem);

        reidratada.Should().BeEquivalentTo(ordem, ComDatasEmMilissegundos);
        reidratada.Status.Should().Be(status);
    }

    [Fact]
    public void OrdemComApontamentosEEtapasInterrompidas_DeveReidratarAsColecoesInternas()
    {
        var builder = new OrdemDeProducaoBuilder().DoMecanico("mecanico-7");
        var ordem = builder.EmExecucao();
        ordem.RegistrarApontamento("mecanico-7", "Troquei o filtro.", DateTimeOffset.UtcNow);
        ordem.Inviabilizar("mecanico-7", "Peça sem fornecedor.", DateTimeOffset.UtcNow.AddMinutes(1));

        var reidratada = IdaEVolta(ordem);

        reidratada.Etapas.Should().HaveCount(2);
        reidratada.Etapas.Last().Status.Should().Be(StatusEtapa.Interrompida);
        reidratada.Apontamentos.Should().ContainSingle().Which.Texto.Should().Be("Troquei o filtro.");
        reidratada.InviabilizadaPor.Should().Be("mecanico-7");
        reidratada.Should().BeEquivalentTo(ordem, ComDatasEmMilissegundos);
    }

    [Fact]
    public void OrdemReidratada_DeveContinuarAceitandoTransicoesDoDominio()
    {
        var reidratada = IdaEVolta(new OrdemDeProducaoBuilder().DoMecanico("m1").EmDiagnostico());

        reidratada.RegistrarApontamento("m1", "Ruído no eixo.", DateTimeOffset.UtcNow);
        reidratada.ConcluirDiagnostico("m1", new LaudoBuilder().Build(), DateTimeOffset.UtcNow);

        reidratada.Status.Should().Be(StatusProducao.DiagnosticoConcluido);
        reidratada.Etapas.Single().Status.Should().Be(StatusEtapa.Concluida);
    }

    [Fact]
    public void Version_DeveIrEVoltarComoLong()
    {
        var ordem = new OrdemDeProducaoBuilder().NaFila();
        ordem.IncrementVersion();
        ordem.IncrementVersion();

        var documento = ordem.ToBsonDocument();

        documento["version"].BsonType.Should().Be(BsonType.Int64);
        IdaEVolta(ordem).Version.Should().Be(2);
    }

    [Fact]
    public void DocumentoDaOrdem_DeveUsarIdGuidStandardDiscriminadorCamelCaseEEnumComoTexto()
    {
        var ordem = new OrdemDeProducaoBuilder().AguardandoExecucao();

        var documento = ordem.ToBsonDocument();

        var id = documento["_id"].AsBsonBinaryData;
        id.SubType.Should().Be(BsonBinarySubType.UuidStandard);
        id.ToGuid().Should().Be(ordem.Id);
        documento[MongoMappings.CampoDiscriminador].AsString.Should().Be(MongoMappings.TipoOrdem);
        documento["status"].AsString.Should().Be("AguardandoExecucao");
        documento["enfileiradaEm"].BsonType.Should().Be(BsonType.DateTime);
        documento["liberadaEm"].BsonType.Should().Be(BsonType.DateTime);
        documento["etapas"].AsBsonArray.Single()["nome"].AsString.Should().Be("Diagnostico");
        documento["veiculo"].AsBsonDocument.Names.Should().BeEquivalentTo("placa", "marca", "modelo");
        documento["servicos"].AsBsonArray[0].AsBsonDocument["precoUnitario"].BsonType.Should().Be(BsonType.Decimal128);
        documento.Names.Should().OnlyContain(nome => nome.StartsWith('_') || char.IsLower(nome[0]));
    }

    [Fact]
    public void CamposOpcionaisNulos_NaoDevemSerGravados()
    {
        var ordem = new OrdemDeProducaoBuilder().SemObservacoes().NaFila();

        var documento = ordem.ToBsonDocument();

        documento.Contains("observacoes").Should().BeFalse();
        documento.Contains("mecanicoId").Should().BeFalse();
        documento.Contains("laudo").Should().BeFalse();
        documento.Contains("liberadaEm").Should().BeFalse();
        IdaEVolta(ordem).Observacoes.Should().BeNull();
    }

    [Fact]
    public void LaudoSemHorasEstimadas_DeveFazerIdaEVolta()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico("m1").EmDiagnostico();
        ordem.ConcluirDiagnostico("m1", new LaudoBuilder().ComHorasEstimadas(null).Build(), DateTimeOffset.UtcNow);

        var reidratada = IdaEVolta(ordem);

        reidratada.Laudo!.HorasEstimadas.Should().BeNull();
        reidratada.Laudo.Texto.Should().Be(ordem.Laudo!.Texto);
    }

    [Fact]
    public void CancelamentoPrevio_DeveFazerIdaEVoltaComIdIgualAoDaOrdemEDiscriminadorProprio()
    {
        var ordemDeServicoId = Guid.NewGuid();
        var tombstone = new CancelamentoPrevioBuilder().DaOrdemDeServico(ordemDeServicoId).ComMotivo("Pagamento cancelado").Build();

        var documento = tombstone.ToBsonDocument();
        var reidratado = BsonSerializer.Deserialize<CancelamentoPrevio>(documento);

        documento["_id"].AsBsonBinaryData.ToGuid().Should().Be(ordemDeServicoId);
        documento[MongoMappings.CampoDiscriminador].AsString.Should().Be(MongoMappings.TipoCancelamentoPrevio);
        reidratado.Should().BeEquivalentTo(tombstone, ComDatasEmMilissegundos);
    }

    [Fact]
    public void Registrar_ChamadoVariasVezes_DeveSerIdempotente()
    {
        var acao = () =>
        {
            MongoMappings.Registrar();
            MongoMappings.Registrar();
        };

        acao.Should().NotThrow();
    }

    [Fact]
    public void OrdemSemColecaoObrigatoria_DeveFalharAoReidratar()
    {
        var documento = new OrdemDeProducaoBuilder().NaFila().ToBsonDocument();
        documento.Remove("etapas");

        var acao = () => BsonSerializer.Deserialize<OrdemDeProducao>(documento);

        acao.Should().Throw<FormatException>();
    }
}
