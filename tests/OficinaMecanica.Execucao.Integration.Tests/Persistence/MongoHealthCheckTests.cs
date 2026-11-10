using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Driver;
using OficinaMecanica.Execucao.Infrastructure.Persistence;

namespace OficinaMecanica.Execucao.Integration.Tests.Persistence;

public class MongoHealthCheckTests
{
    // Servidor "mudo": o TCP e aceito e nenhuma resposta volta. O MongoClient usa os timeouts padrao do driver (30 s de
    // selecao de servidor), entao so o limite do proprio check pode encerrar a espera. E esse limite que sustenta o
    // timeoutSeconds: 5 da readiness no deployment.yaml.
    [Fact]
    public async Task BancoQueNaoResponde_DeveResponderUnhealthyNoLimiteDeclaradoDe3Segundos()
    {
        using var servidor = new TcpListener(IPAddress.Loopback, 0);
        servidor.Start();
        var porta = ((IPEndPoint)servidor.LocalEndpoint).Port;
        using var cliente = new MongoClient($"mongodb://127.0.0.1:{porta}/?directConnection=true");
        var check = new MongoHealthCheck(cliente.GetDatabase("execucao"));

        var relogio = Stopwatch.StartNew();
        var resultado = await check.CheckHealthAsync(new HealthCheckContext());
        relogio.Stop();

        resultado.Status.Should().Be(HealthStatus.Unhealthy);
        resultado.Description.Should().Be("MongoDB indisponível.");
        resultado.Exception.Should().NotBeNull();
        // Mesmo critério do RabbitMqHealthCheck: acima de 2,5 s mostra que o limite e o de 3 s (e nao um menor) e abaixo de
        // 3,8 s que um limite maior (4 s, 30 s do driver) estouraria.
        relogio.Elapsed.Should().BeGreaterThan(TimeSpan.FromSeconds(2.5)).And.BeLessThan(TimeSpan.FromSeconds(3.8));
    }

    // Quem aborta a requisicao nao e o banco: o cancelamento do chamador precisa subir como excecao. Devolvido como
    // Unhealthy, ele viraria um 503 falso (e, com o HealthCheckComCache na frente, ficaria guardado por 5 s).
    [Fact]
    public async Task TokenDoChamadorJaCancelado_DevePropagarEmVezDeVirarUnhealthy()
    {
        using var servidor = new TcpListener(IPAddress.Loopback, 0);
        servidor.Start();
        var porta = ((IPEndPoint)servidor.LocalEndpoint).Port;
        using var cliente = new MongoClient($"mongodb://127.0.0.1:{porta}/?directConnection=true");
        var check = new MongoHealthCheck(cliente.GetDatabase("execucao"));
        using var chamador = new CancellationTokenSource();
        await chamador.CancelAsync();

        var acao = () => check.CheckHealthAsync(new HealthCheckContext(), chamador.Token);

        await acao.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ChamadorQueCancelaNoMeioDaEspera_DevePropagarEmVezDeVirarUnhealthy()
    {
        using var servidor = new TcpListener(IPAddress.Loopback, 0);
        servidor.Start();
        var porta = ((IPEndPoint)servidor.LocalEndpoint).Port;
        using var cliente = new MongoClient($"mongodb://127.0.0.1:{porta}/?directConnection=true");
        var check = new MongoHealthCheck(cliente.GetDatabase("execucao"));
        using var chamador = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var relogio = Stopwatch.StartNew();
        var acao = () => check.CheckHealthAsync(new HealthCheckContext(), chamador.Token);

        await acao.Should().ThrowAsync<OperationCanceledException>();
        relogio.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2.5), "o cancelamento do chamador interrompe a espera antes do limite de 3 s");
    }
}
