using FluentAssertions;
using OficinaMecanica.Contracts.Events;
using OficinaMecanica.Execucao.Application.Commands.ConcluirDiagnostico;
using OficinaMecanica.Execucao.Application.Interfaces;
using OficinaMecanica.Execucao.Domain.Entities;
using OficinaMecanica.Execucao.Domain.Exceptions;
using OficinaMecanica.Execucao.Domain.Interfaces;
using OficinaMecanica.Execucao.Tests.Common;

namespace OficinaMecanica.Execucao.Application.Tests.Commands;

public class ConcluirDiagnosticoCommandHandlerTests
{
    private const string Mecanico = "mecanico-01";
    private static readonly DateTimeOffset Agora = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IOrdemDeProducaoRepository> _repositorioMock = new();
    private readonly Mock<IIntegrationEventPublisher> _publisherMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly List<string> _chamadas = new();
    private readonly ConcluirDiagnosticoCommandHandler _handler;

    public ConcluirDiagnosticoCommandHandlerTests()
    {
        _repositorioMock
            .Setup(r => r.AtualizarAsync(It.IsAny<OrdemDeProducao>(), It.IsAny<CancellationToken>()))
            .Callback(() => _chamadas.Add("atualizar"))
            .Returns(Task.CompletedTask);
        _publisherMock
            .Setup(p => p.PublishAsync(It.IsAny<DiagnosticoConcluido>(), It.IsAny<CancellationToken>()))
            .Callback(() => _chamadas.Add("publicar"))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => _chamadas.Add("salvar"))
            .Returns(Task.CompletedTask);

        _handler = new ConcluirDiagnosticoCommandHandler(
            _repositorioMock.Object, _publisherMock.Object, _unitOfWorkMock.Object, new RelogioFixo(Agora));
    }

    [Fact]
    public async Task Handle_PeloMecanicoResponsavel_DeveConcluirEPublicarDiagnosticoConcluido()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico(Mecanico).EmDiagnostico();
        OrdemExiste(ordem);

        var resultado = await _handler.Handle(
            new ConcluirDiagnosticoCommand(ordem.Id, Mecanico, "Troca de óleo e filtro recomendada", 1.5m),
            CancellationToken.None);

        resultado.Status.Should().Be("DiagnosticoConcluido");
        resultado.Laudo.Should().NotBeNull();
        resultado.Laudo!.Texto.Should().Be("Troca de óleo e filtro recomendada");
        resultado.Laudo.HorasEstimadas.Should().Be(1.5m);
        _publisherMock.Verify(
            p => p.PublishAsync(
                It.Is<DiagnosticoConcluido>(e =>
                    e.OrdemDeServicoId == ordem.Id
                    && e.MecanicoId == Mecanico
                    && e.Laudo == "Troca de óleo e filtro recomendada"
                    && e.OcorridoEm == Agora
                    && e.MessageId != Guid.Empty),
                It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_DevePublicarAntesDeSalvar()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico(Mecanico).EmDiagnostico();
        OrdemExiste(ordem);

        await _handler.Handle(new ConcluirDiagnosticoCommand(ordem.Id, Mecanico, "Laudo"), CancellationToken.None);

        _chamadas.Should().Equal("atualizar", "publicar", "salvar");
    }

    [Fact]
    public async Task Handle_OLaudoPublicadoDeveSerOTextoJaNormalizado()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico(Mecanico).EmDiagnostico();
        OrdemExiste(ordem);

        await _handler.Handle(new ConcluirDiagnosticoCommand(ordem.Id, Mecanico, "  Laudo com espaços  "), CancellationToken.None);

        _publisherMock.Verify(
            p => p.PublishAsync(It.Is<DiagnosticoConcluido>(e => e.Laudo == "Laudo com espaços"), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_OrdemInexistente_DeveLancarKeyNotFoundSemPublicarNemSalvar()
    {
        _repositorioMock
            .Setup(r => r.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrdemDeProducao?)null);

        var act = () => _handler.Handle(
            new ConcluirDiagnosticoCommand(Guid.NewGuid(), Mecanico, "Laudo"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*não encontrada*");
        _chamadas.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_PorOutroMecanico_DeveLancarDomainExceptionSemPublicarNemSalvar()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico(Mecanico).EmDiagnostico();
        OrdemExiste(ordem);

        var act = () => _handler.Handle(
            new ConcluirDiagnosticoCommand(ordem.Id, "outro-mecanico", "Laudo"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*mecânico responsável*");
        _chamadas.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_OrdemForaDeEmDiagnostico_DeveLancarDomainExceptionSemPublicarNemSalvar()
    {
        var ordem = new OrdemDeProducaoBuilder().NaFila();
        OrdemExiste(ordem);

        var act = () => _handler.Handle(
            new ConcluirDiagnosticoCommand(ordem.Id, Mecanico, "Laudo"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*concluir o diagnóstico*");
        _chamadas.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ComLaudoVazio_DeveLancarDomainExceptionSemPublicarNemSalvar()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico(Mecanico).EmDiagnostico();
        OrdemExiste(ordem);

        var act = () => _handler.Handle(
            new ConcluirDiagnosticoCommand(ordem.Id, Mecanico, " "), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*texto do laudo*");
        _chamadas.Should().BeEmpty();
    }

    private void OrdemExiste(OrdemDeProducao ordem) =>
        _repositorioMock
            .Setup(r => r.ObterPorIdAsync(ordem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ordem);
}
