using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OficinaMecanica.Execucao.Application;
using OficinaMecanica.Execucao.Application.Commands.CancelarProducao;
using OficinaMecanica.Execucao.Application.Commands.ReivindicarProximoDiagnostico;
using OficinaMecanica.Execucao.Application.Interfaces;
using OficinaMecanica.Execucao.Application.Queries.ObterResumoDaFila;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Domain.Interfaces;
using OficinaMecanica.Execucao.Tests.Common;

namespace OficinaMecanica.Execucao.Application.Tests;

public class DependencyInjectionTests
{
    private readonly Mock<IOrdemDeProducaoRepository> _repositorioMock = new();

    private ServiceProvider MontarProvider(Action<IServiceCollection>? ajustes = null)
    {
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton(_repositorioMock.Object);
        services.AddSingleton(new Mock<ICancelamentoPrevioRepository>().Object);
        services.AddSingleton(new Mock<IUnitOfWork>().Object);
        services.AddSingleton(new Mock<IIntegrationEventPublisher>().Object);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        ajustes?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task AddApplication_DeveResolverOMediatorEExecutarUmaQuery()
    {
        _repositorioMock
            .Setup(r => r.ContarPorStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<StatusProducao, int> { [StatusProducao.Fila] = 3 });
        using var provider = MontarProvider();

        var resultado = await provider.GetRequiredService<IMediator>().Send(new ObterResumoDaFilaQuery());

        resultado.PorStatus["Fila"].Should().Be(3);
    }

    [Fact]
    public async Task AddApplication_DeveAplicarOValidationBehaviorAntesDoHandler()
    {
        using var provider = MontarProvider();

        var act = () => provider.GetRequiredService<IMediator>().Send(new ReivindicarProximoDiagnosticoCommand(""));

        await act.Should().ThrowAsync<ValidationException>();
        _repositorioMock.Verify(
            r => r.ObterProximaDaFilaAsync(It.IsAny<StatusProducao>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public void AddApplication_DeveRegistrarOsValidatorsDosCommands()
    {
        using var provider = MontarProvider();

        provider.GetService<IValidator<CancelarProducaoCommand>>().Should().BeOfType<CancelarProducaoCommandValidator>();
    }

    [Fact]
    public void AddApplication_SemRelogioRegistrado_DeveUsarOTimeProviderDoSistema()
    {
        using var provider = MontarProvider();

        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(TimeProvider.System);
    }

    [Fact]
    public void AddApplication_ComRelogioJaRegistrado_DeveManterOdoHost()
    {
        var relogio = new RelogioFixo(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero));
        using var provider = MontarProvider(services => services.AddSingleton<TimeProvider>(relogio));

        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(relogio);
    }
}
