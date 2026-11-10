using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Moq;
using OficinaMecanica.Execucao.Infrastructure.Messaging;
using OficinaMecanica.Execucao.Integration.Tests.Support;
using RabbitMQ.Client;

namespace OficinaMecanica.Execucao.Integration.Tests.Messaging;

public class RabbitMqHealthCheckTests
{
    private static IOptions<RabbitMqOptions> Opcoes(ushort porta) =>
        Options.Create(new RabbitMqOptions
        {
            Host = "127.0.0.1",
            Port = porta,
            Username = "svc",
            Password = "senha-de-teste"
        });

    private static RabbitMqHealthCheck CriarCheck(ushort porta) => new(Opcoes(porta));

    private static TcpListener SubirBrokerCongelado()
    {
        // O kernel aceita a conexao de um processo parado e o handshake AMQP nunca recebe resposta: e o broker
        // "congelado", o caso em que o limite do check precisa valer de verdade.
        var servidor = new TcpListener(IPAddress.Loopback, 0);
        servidor.Start();
        return servidor;
    }

    private static ushort PortaDe(TcpListener servidor) => (ushort)((IPEndPoint)servidor.LocalEndpoint).Port;

    // Os testes com uma tentativa que so o teste conclui (TaskCompletionSource) passam por aqui: sem o WaitAsync de 3 s do check
    // a avaliacao nunca termina, e o teto transforma esse travamento numa falha clara em vez de pendurar o run.
    private static Task<HealthCheckResult> AvaliarAsync(RabbitMqHealthCheck check, CancellationToken chamador = default) =>
        check.CheckHealthAsync(new HealthCheckContext(), chamador)
            .ComTetoAsync("o check deve devolver Degraded no limite de 3 s mesmo com a tentativa de conexao pendente (WaitAsync)");

    private static Mock<IConnection> ConexaoQueAvisaQuandoFechada(TaskCompletionSource fechada)
    {
        var conexao = new Mock<IConnection>();
        conexao.Setup(c => c.DisposeAsync()).Returns(() =>
        {
            fechada.TrySetResult();
            return ValueTask.CompletedTask;
        });
        return conexao;
    }

    [Fact]
    public async Task BrokerInalcancavel_DeveResponderDegradedEmVezDeUnhealthy()
    {
        var check = CriarCheck(porta: 1);

        var resultado = await check.CheckHealthAsync(new HealthCheckContext());

        resultado.Status.Should().Be(HealthStatus.Degraded);
        resultado.Description.Should().Contain("outbox");
        resultado.Exception.Should().NotBeNull();
    }

    [Fact]
    public async Task BrokerCongelado_DeveResponderDegradedNoLimiteDeclaradoDe3Segundos()
    {
        using var servidor = SubirBrokerCongelado();
        var check = CriarCheck(PortaDe(servidor));

        var relogio = Stopwatch.StartNew();
        var resultado = await check.CheckHealthAsync(new HealthCheckContext());
        relogio.Stop();

        resultado.Status.Should().Be(HealthStatus.Degraded);
        // Sem o WaitAsync o cliente leva ~8 s para desistir; com ele a resposta sai em ~3 s. O teto de 3,8 s separa o limite
        // de 3 s de um limite maior (um WaitAsync de 4 s estoura) e deixa folga para a maquina carregada.
        relogio.Elapsed.Should().BeGreaterThan(TimeSpan.FromSeconds(2.5)).And.BeLessThan(TimeSpan.FromSeconds(3.8),
            "o limite do check e 3 s e a readiness do k8s espera 5 s");
    }

    [Fact]
    public async Task CancelamentoDoChamador_DevePropagarEmVezDeVirarDegraded()
    {
        using var servidor = SubirBrokerCongelado();
        var check = CriarCheck(PortaDe(servidor));
        using var chamador = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var acao = () => check.CheckHealthAsync(new HealthCheckContext(), chamador.Token)
            .ComTetoAsync("o cancelamento do chamador deve interromper a espera pelo broker congelado");

        await acao.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ConexaoEstabelecidaDentroDoLimite_DeveResponderHealthyEFecharAConexao()
    {
        var fechada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var conexao = ConexaoQueAvisaQuandoFechada(fechada);
        var check = new RabbitMqHealthCheck(Opcoes(5672), (_, _) => Task.FromResult(conexao.Object));

        var resultado = await check.CheckHealthAsync(new HealthCheckContext());

        resultado.Status.Should().Be(HealthStatus.Healthy);
        fechada.Task.IsCompleted.Should().BeTrue("o check abre a conexao so para provar o broker e a fecha");
        conexao.Verify(c => c.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task ConexaoQueCompletaDepoisDoLimite_DeveSerFechadaEmVezDeVazar()
    {
        var fechada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var conexao = ConexaoQueAvisaQuandoFechada(fechada);
        var tardia = new TaskCompletionSource<IConnection>(TaskCreationOptions.RunContinuationsAsynchronously);
        var check = new RabbitMqHealthCheck(Opcoes(5672), (_, _) => tardia.Task);

        var resultado = await AvaliarAsync(check);
        resultado.Status.Should().Be(HealthStatus.Degraded, "a conexao ainda nao tinha completado no limite");
        fechada.Task.IsCompleted.Should().BeFalse();

        tardia.SetResult(conexao.Object);

        (await Task.WhenAny(fechada.Task, Task.Delay(TimeSpan.FromSeconds(5)))).Should().BeSameAs(fechada.Task,
            "a conexao que chega depois do limite precisa ser fechada, senao vaza uma conexao por avaliacao");
        conexao.Verify(c => c.DisposeAsync(), Times.Once);
    }

    // Cancelar o chamador abandona a tentativa do mesmo jeito que o limite de 3 s: se o broker congelado completar o
    // handshake depois, a conexao tem de ser fechada (senao cada requisicao abortada vaza uma).
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChamadorQueCancelaEConexaoQueCompletaDepois_DeveSerFechadaEmVezDeVazar(bool jaCancelado)
    {
        var fechada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var conexao = ConexaoQueAvisaQuandoFechada(fechada);
        var tardia = new TaskCompletionSource<IConnection>(TaskCreationOptions.RunContinuationsAsynchronously);
        var check = new RabbitMqHealthCheck(Opcoes(5672), (_, _) => tardia.Task);
        using var chamador = new CancellationTokenSource();
        if (jaCancelado)
            await chamador.CancelAsync();
        else
            chamador.CancelAfter(TimeSpan.FromMilliseconds(100));

        var acao = () => AvaliarAsync(check, chamador.Token);
        await acao.Should().ThrowAsync<OperationCanceledException>("quem cancelou foi o chamador, nao o limite do check");
        fechada.Task.IsCompleted.Should().BeFalse();

        tardia.SetResult(conexao.Object);

        (await Task.WhenAny(fechada.Task, Task.Delay(TimeSpan.FromSeconds(5)))).Should().BeSameAs(fechada.Task,
            "a tentativa abandonada pelo cancelamento do chamador tambem precisa ser fechada quando completar");
        conexao.Verify(c => c.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task TentativaTardiaQueFalha_NaoDeveAlterarOResultado()
    {
        var tardia = new TaskCompletionSource<IConnection>(TaskCreationOptions.RunContinuationsAsynchronously);
        var check = new RabbitMqHealthCheck(Opcoes(5672), (_, _) => tardia.Task);

        var resultado = await AvaliarAsync(check);
        tardia.SetException(new IOException("conexao recusada depois do limite"));
        await Task.Delay(100);

        resultado.Status.Should().Be(HealthStatus.Degraded);
    }

    // A falha da tentativa abandonada precisa ser OBSERVADA em segundo plano (o catch de FecharSeConectarDepoisAsync); senao a
    // excecao vira UnobservedTaskException quando a tarefa e coletada. O teste solta todas as referencias (a avaliacao roda
    // num metodo a parte), forca a coleta e confere que o evento nao viu a excecao DESTE teste: o filtro pela instancia isola
    // os demais testes que rodem em paralelo ou deixem excecoes pendentes de outros.
    [Fact]
    public async Task TentativaTardiaQueFalha_NaoDeveDeixarAExcecaoSemObservar()
    {
        var minhaExcecao = new IOException("conexao recusada depois do limite (nao observada?)");
        // O evento dispara na thread do finalizador: a leitura e a escrita passam por Volatile/Interlocked.
        var naoObservada = 0;
        void Observar(object? origem, UnobservedTaskExceptionEventArgs e)
        {
            if (e.Exception.Flatten().InnerExceptions.Contains(minhaExcecao))
                Interlocked.Exchange(ref naoObservada, 1);
        }

        TaskScheduler.UnobservedTaskException += Observar;
        try
        {
            (await AvaliarEFalharDepoisDoLimiteAsync(minhaExcecao)).Should().Be(HealthStatus.Degraded);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.WaitForPendingFinalizers();

            (Volatile.Read(ref naoObservada) == 1).Should().BeFalse("a falha tardia da tentativa abandonada e absorvida em segundo plano, nao fica sem observar");
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Observar;
        }
    }

    // Sem inlining e fora do metodo do teste: quando ele retorna, nada mais referencia o check nem a tarefa da conexao, e a
    // coleta pode finalizar a tarefa (e disparar UnobservedTaskException se ninguem observou a falha).
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<HealthStatus> AvaliarEFalharDepoisDoLimiteAsync(Exception falha)
    {
        var tardia = new TaskCompletionSource<IConnection>(TaskCreationOptions.RunContinuationsAsynchronously);
        var check = new RabbitMqHealthCheck(Opcoes(5672), (_, _) => tardia.Task);

        var resultado = await AvaliarAsync(check);
        tardia.SetException(falha);
        await Task.Delay(200);

        return resultado.Status;
    }
}
