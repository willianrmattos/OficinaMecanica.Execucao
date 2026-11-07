using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OficinaMecanica.Execucao.Integration.Tests.Support;

namespace OficinaMecanica.Execucao.Integration.Tests.Infra;

// Decisao de desenho: broker fora NAO tira o pod de rotacao (o outbox continua aceitando escrita), banco fora tira.
// Cada teste sobe e derruba a propria infraestrutura, porque aqui os containers sao parados de verdade.
[Trait("Category", "Infra")]
public class ProntidaoTests
{
    private sealed record Prontidao(HttpStatusCode Status, string Geral, IReadOnlyDictionary<string, string> Checks);

    private static async Task<Prontidao> LerProntidaoAsync(HttpClient cliente)
    {
        var resposta = await cliente.GetAsync("/health/ready");
        var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;

        return new Prontidao(
            resposta.StatusCode,
            corpo.GetProperty("status").GetString()!,
            corpo.GetProperty("checks").EnumerateObject().ToDictionary(c => c.Name, c => c.Value.GetProperty("status").GetString()!));
    }

    [Fact(Timeout = 300_000)]
    public async Task BrokerParado_DeveManterProntidaoEm200ComOBusDegradedEContinuarAceitandoEscrita()
    {
        var infra = new InfraDedicada();
        await infra.InitializeAsync();
        try
        {
            await using var ambiente = await AmbienteDeInfra.CriarAsync(infra);
            var cliente = ambiente.Cliente();

            var saudavel = await LerProntidaoAsync(cliente);
            saudavel.Status.Should().Be(HttpStatusCode.OK);
            saudavel.Geral.Should().Be("Healthy");
            saudavel.Checks.Should().Contain(new KeyValuePair<string, string>("mongodb", "Healthy"))
                .And.Contain(new KeyValuePair<string, string>("rabbitmq", "Healthy"))
                .And.Contain(new KeyValuePair<string, string>("masstransit-bus", "Healthy"));

            await infra.PararRabbitAsync();

            Prontidao? degradada = null;
            (await Esperar.AteAsync(async () =>
            {
                degradada = await LerProntidaoAsync(cliente);
                degradada.Status.Should().Be(HttpStatusCode.OK, "o broker fora nao pode tirar o pod de rotacao");
                return degradada.Geral == "Degraded";
            }, limiteEmMs: 60_000, intervaloEmMs: 500)).Should().BeTrue();

            degradada!.Checks["rabbitmq"].Should().Be("Degraded");
            degradada.Checks["mongodb"].Should().Be("Healthy");
            degradada.Checks["masstransit-bus"].Should().NotBe("Unhealthy");
            (await cliente.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);

            // O servico segue aceitando escrita: o claim grava a ordem e a mensagem fica no outbox ate o broker voltar.
            var ordem = new OrdemDeProducaoBuilder().NaFila();
            await ambiente.SemearAsync(ordem);
            var claim = await cliente.PostAsync("/api/diagnosticos/proximo", null);
            claim.StatusCode.Should().Be(HttpStatusCode.OK);
            (await ambiente.DocumentoDaOrdemAsync(ordem.Id))!["status"].AsString.Should().Be("EmDiagnostico");
            (await ambiente.ContarNoBancoAsync("outbox.messages")).Should().Be(1);
        }
        finally
        {
            await infra.DisposeAsync();
        }
    }

    [Fact(Timeout = 300_000)]
    public async Task MongoParado_DeveTirarOPodDeRotacaoCom503SemDerrubarOLiveness()
    {
        var infra = new InfraDedicada();
        await infra.InitializeAsync();
        try
        {
            await using var ambiente = await AmbienteDeInfra.CriarAsync(infra);
            ambiente.Api.LimparBancoAoDescartar = false;
            var cliente = ambiente.Cliente();
            (await LerProntidaoAsync(cliente)).Status.Should().Be(HttpStatusCode.OK);

            await infra.PararMongoAsync();

            var pronta = await LerProntidaoAsync(cliente);
            pronta.Status.Should().Be(HttpStatusCode.ServiceUnavailable);
            pronta.Geral.Should().Be("Unhealthy");
            pronta.Checks["mongodb"].Should().Be("Unhealthy");
            (await cliente.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK, "liveness nao depende do banco");
        }
        finally
        {
            await infra.DisposeAsync();
        }
    }
}
