using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using OficinaMecanica.Contracts.Events;
using OficinaMecanica.Execucao.Application.Interfaces;
using OficinaMecanica.Execucao.Infrastructure.Messaging;

namespace OficinaMecanica.Execucao.Integration.Tests.Messaging;

public class MassTransitIntegrationEventPublisherTests : IAsyncLifetime
{
    // Mensagem sem MessageId: o publicador nao pode inventar um nem falhar.
    public sealed record EventoSemMessageId(Guid OrdemDeServicoId);

    private ServiceProvider _provider = null!;
    private ITestHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _provider = new ServiceCollection()
            .AddMassTransitTestHarness()
            .AddScoped<IIntegrationEventPublisher, MassTransitIntegrationEventPublisher>()
            .BuildServiceProvider(true);
        _harness = _provider.GetRequiredService<ITestHarness>();
        await _harness.Start();
    }

    public async Task DisposeAsync()
    {
        await _harness.Stop();
        await _provider.DisposeAsync();
    }

    private IIntegrationEventPublisher Publicador() =>
        _provider.CreateScope().ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();

    [Fact]
    public async Task PublishAsync_DeveCopiarOMessageIdDoContratoParaOEnvelope()
    {
        var evento = new DiagnosticoIniciado(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, "mecanico-1");

        await Publicador().PublishAsync(evento);

        var publicada = _harness.Published.Select<DiagnosticoIniciado>().Single();
        publicada.Context.MessageId.Should().Be(evento.MessageId);
        publicada.Context.Message.Should().Be(evento);
    }

    [Fact]
    public async Task PublishAsync_ParaCadaEventoDoContrato_DeveUsarOMessageIdDele()
    {
        var ordem = Guid.NewGuid();
        var concluido = new DiagnosticoConcluido(Guid.NewGuid(), ordem, DateTimeOffset.UtcNow, "m1", "laudo");
        var finalizada = new ExecucaoFinalizada(Guid.NewGuid(), ordem, DateTimeOffset.UtcNow, "resumo", DateTimeOffset.UtcNow);
        var inviavel = new ExecucaoInviavel(Guid.NewGuid(), ordem, DateTimeOffset.UtcNow, "motivo");
        var publicador = Publicador();

        await publicador.PublishAsync(concluido);
        await publicador.PublishAsync(finalizada);
        await publicador.PublishAsync(inviavel);

        _harness.Published.Select<DiagnosticoConcluido>().Single().Context.MessageId.Should().Be(concluido.MessageId);
        _harness.Published.Select<ExecucaoFinalizada>().Single().Context.MessageId.Should().Be(finalizada.MessageId);
        _harness.Published.Select<ExecucaoInviavel>().Single().Context.MessageId.Should().Be(inviavel.MessageId);
    }

    [Fact]
    public async Task PublishAsync_RepetindoOTipo_DeveContinuarCopiandoOMessageIdDeCadaMensagem()
    {
        var primeiro = new DiagnosticoIniciado(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, "m1");
        var segundo = new DiagnosticoIniciado(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, "m2");
        var publicador = Publicador();

        await publicador.PublishAsync(primeiro);
        await publicador.PublishAsync(segundo);

        _harness.Published.Select<DiagnosticoIniciado>().Select(p => p.Context.MessageId)
            .Should().BeEquivalentTo(new Guid?[] { primeiro.MessageId, segundo.MessageId });
    }

    [Fact]
    public async Task PublishAsync_DeMensagemSemMessageId_DevePublicarSemFalharEComIdGeradoPeloMassTransit()
    {
        await Publicador().PublishAsync(new EventoSemMessageId(Guid.NewGuid()));

        var publicada = _harness.Published.Select<EventoSemMessageId>().Single();
        publicada.Context.MessageId.Should().NotBeNull().And.NotBe(Guid.Empty);
    }

    [Fact]
    public async Task PublishAsync_ComMensagemNula_DeveLancarArgumentNullException()
    {
        var acao = () => Publicador().PublishAsync<DiagnosticoIniciado>(null!);

        await acao.Should().ThrowAsync<ArgumentNullException>();
    }
}
