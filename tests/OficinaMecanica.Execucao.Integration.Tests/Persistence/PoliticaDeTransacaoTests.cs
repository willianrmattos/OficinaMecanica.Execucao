using MediatR;
using OficinaMecanica.Execucao.Application;
using OficinaMecanica.Execucao.Application.Commands.ReivindicarProximoReparo;
using OficinaMecanica.Execucao.Application.Interfaces;
using OficinaMecanica.Execucao.Infrastructure.Persistence;

namespace OficinaMecanica.Execucao.Integration.Tests.Persistence;

public class PoliticaDeTransacaoTests
{
    private static IEnumerable<(Type Requisicao, Type Handler)> Handlers() =>
        typeof(DependencyInjection).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .SelectMany(handler => handler.GetInterfaces()
                .Where(i => i.IsGenericType
                            && (i.GetGenericTypeDefinition() == typeof(IRequestHandler<>)
                                || i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)))
                .Select(i => (Requisicao: i.GetGenericArguments()[0], Handler: handler)));

    private static bool PublicaNoOutbox(Type handler) =>
        handler.GetConstructors().Single().GetParameters().Any(p => p.ParameterType == typeof(IIntegrationEventPublisher));

    [Fact]
    public void RequisicoesTransacionais_DevemSerExatamenteOsHandlersQuePublicamNoOutbox()
    {
        var quePublicam = Handlers().Where(h => PublicaNoOutbox(h.Handler)).Select(h => h.Requisicao).ToList();

        quePublicam.Should().NotBeEmpty();
        PoliticaDeTransacao.RequisicoesTransacionais.Should().BeEquivalentTo(quePublicam);
    }

    [Fact]
    public void Queries_NuncaDevemAbrirTransacao()
    {
        var queries = Handlers().Select(h => h.Requisicao).Where(r => r.Namespace!.Contains(".Queries.")).ToList();

        queries.Should().NotBeEmpty();
        queries.Should().OnlyContain(q => !PoliticaDeTransacao.Exige(q));
    }

    [Fact]
    public void ClaimDeReparo_NaoPublicaNadaEPorIssoFicaForaDaTransacao()
    {
        PoliticaDeTransacao.Exige(typeof(ReivindicarProximoReparoCommand)).Should().BeFalse();
    }
}
