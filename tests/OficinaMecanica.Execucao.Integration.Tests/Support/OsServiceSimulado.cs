using System.Collections.Concurrent;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using OficinaMecanica.Contracts;
using OficinaMecanica.Contracts.Commands;
using OficinaMecanica.Contracts.Events;

namespace OficinaMecanica.Execucao.Integration.Tests.Support;

public sealed record MensagemRecebida(string Tipo, Guid MessageId, Guid OrdemDeServicoId);

// Faz o papel do OS Service (a saga) nos testes de infra: envia os comandos ao endpoint da Execucao e
// registra os eventos que a Execucao publica. E um bus independente, sem outbox, no mesmo virtual host.
// Precisa subir ANTES da API para a fila de eventos ja existir e estar ligada aos exchanges.
public sealed class OsServiceSimulado : IAsyncDisposable
{
    public const string FilaDeEventos = "os-saga-simulada";

    private readonly ServiceProvider _provider;
    private readonly IBusControl _bus;

    public GravadorDeMensagens Gravador { get; }

    private OsServiceSimulado(ServiceProvider provider, IBusControl bus, GravadorDeMensagens gravador)
    {
        _provider = provider;
        _bus = bus;
        Gravador = gravador;
    }

    public static async Task<OsServiceSimulado> IniciarAsync(IEndpointsDeInfra infra, string virtualHost)
    {
        var gravador = new GravadorDeMensagens();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(gravador);
        services.AddMassTransit(x =>
        {
            x.AddConsumer<GravadorDeMensagensConsumer>();
            x.UsingRabbitMq((contexto, bus) =>
            {
                bus.Host(infra.HostDoRabbit, (ushort)infra.PortaDoRabbit, virtualHost, host =>
                {
                    host.Username(infra.UsuarioDoRabbit);
                    host.Password(infra.SenhaDoRabbit);
                });
                bus.ReceiveEndpoint(FilaDeEventos, endpoint => endpoint.ConfigureConsumer<GravadorDeMensagensConsumer>(contexto));
            });
        });

        var provider = services.BuildServiceProvider();
        var controle = provider.GetRequiredService<IBusControl>();
        await controle.StartAsync();

        return new OsServiceSimulado(provider, controle, gravador);
    }

    public async Task EnviarAsync<T>(T comando) where T : class
    {
        var endpoint = await _bus.GetSendEndpoint(new Uri($"exchange:{Filas.Execucao}"));
        await endpoint.Send(comando, contexto => contexto.MessageId = IdDaMensagem(comando));
    }

    private static Guid IdDaMensagem<T>(T comando) => comando switch
    {
        SolicitarDiagnostico m => m.MessageId,
        IniciarExecucao m => m.MessageId,
        CancelarProducao m => m.MessageId,
        _ => Guid.NewGuid()
    };

    public async ValueTask DisposeAsync()
    {
        await _bus.StopAsync();
        await _provider.DisposeAsync();
    }
}

public sealed class GravadorDeMensagens
{
    private readonly ConcurrentQueue<MensagemRecebida> _recebidas = new();

    public IReadOnlyList<MensagemRecebida> Todas => _recebidas.ToArray();

    public IReadOnlyList<MensagemRecebida> De<T>() => Todas.Where(m => m.Tipo == typeof(T).Name).ToList();

    public int Falhas => Todas.Count(m => m.Tipo.StartsWith("Fault"));

    public void Registrar(string tipo, Guid? messageId, Guid ordemDeServicoId) =>
        _recebidas.Enqueue(new MensagemRecebida(tipo, messageId ?? Guid.Empty, ordemDeServicoId));
}

public sealed class GravadorDeMensagensConsumer(GravadorDeMensagens gravador) :
    IConsumer<DiagnosticoIniciado>,
    IConsumer<DiagnosticoConcluido>,
    IConsumer<ExecucaoFinalizada>,
    IConsumer<ExecucaoInviavel>,
    IConsumer<Fault<SolicitarDiagnostico>>,
    IConsumer<Fault<IniciarExecucao>>,
    IConsumer<Fault<CancelarProducao>>
{
    public Task Consume(ConsumeContext<DiagnosticoIniciado> context) =>
        Gravar(nameof(DiagnosticoIniciado), context.MessageId, context.Message.OrdemDeServicoId);

    public Task Consume(ConsumeContext<DiagnosticoConcluido> context) =>
        Gravar(nameof(DiagnosticoConcluido), context.MessageId, context.Message.OrdemDeServicoId);

    public Task Consume(ConsumeContext<ExecucaoFinalizada> context) =>
        Gravar(nameof(ExecucaoFinalizada), context.MessageId, context.Message.OrdemDeServicoId);

    public Task Consume(ConsumeContext<ExecucaoInviavel> context) =>
        Gravar(nameof(ExecucaoInviavel), context.MessageId, context.Message.OrdemDeServicoId);

    public Task Consume(ConsumeContext<Fault<SolicitarDiagnostico>> context) =>
        Gravar("Fault:" + nameof(SolicitarDiagnostico), context.MessageId, context.Message.Message.OrdemDeServicoId);

    public Task Consume(ConsumeContext<Fault<IniciarExecucao>> context) =>
        Gravar("Fault:" + nameof(IniciarExecucao), context.MessageId, context.Message.Message.OrdemDeServicoId);

    public Task Consume(ConsumeContext<Fault<CancelarProducao>> context) =>
        Gravar("Fault:" + nameof(CancelarProducao), context.MessageId, context.Message.Message.OrdemDeServicoId);

    private Task Gravar(string tipo, Guid? messageId, Guid ordemDeServicoId)
    {
        gravador.Registrar(tipo, messageId, ordemDeServicoId);
        return Task.CompletedTask;
    }
}
