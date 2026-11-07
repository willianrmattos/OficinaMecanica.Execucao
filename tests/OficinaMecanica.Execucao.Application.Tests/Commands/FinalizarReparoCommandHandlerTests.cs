using FluentAssertions;
using OficinaMecanica.Contracts.Events;
using OficinaMecanica.Execucao.Application.Commands.FinalizarReparo;
using OficinaMecanica.Execucao.Application.Interfaces;
using OficinaMecanica.Execucao.Domain.Entities;
using OficinaMecanica.Execucao.Domain.Exceptions;
using OficinaMecanica.Execucao.Domain.Interfaces;
using OficinaMecanica.Execucao.Tests.Common;

namespace OficinaMecanica.Execucao.Application.Tests.Commands;

public class FinalizarReparoCommandHandlerTests
{
    private const string Mecanico = "reparador-01";
    private static readonly DateTimeOffset Agora = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IOrdemDeProducaoRepository> _repositorioMock = new();
    private readonly Mock<IIntegrationEventPublisher> _publisherMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly List<string> _chamadas = new();
    private readonly FinalizarReparoCommandHandler _handler;

    public FinalizarReparoCommandHandlerTests()
    {
        _repositorioMock
            .Setup(r => r.AtualizarAsync(It.IsAny<OrdemDeProducao>(), It.IsAny<CancellationToken>()))
            .Callback(() => _chamadas.Add("atualizar"))
            .Returns(Task.CompletedTask);
        _publisherMock
            .Setup(p => p.PublishAsync(It.IsAny<ExecucaoFinalizada>(), It.IsAny<CancellationToken>()))
            .Callback(() => _chamadas.Add("publicar"))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => _chamadas.Add("salvar"))
            .Returns(Task.CompletedTask);

        _handler = new FinalizarReparoCommandHandler(
            _repositorioMock.Object, _publisherMock.Object, _unitOfWorkMock.Object, new RelogioFixo(Agora));
    }

    [Fact]
    public async Task Handle_PeloMecanicoResponsavel_DeveFinalizarEPublicarExecucaoFinalizada()
    {
        var ordem = new OrdemDeProducaoBuilder().EmExecucao(Mecanico);
        OrdemExiste(ordem);

        var resultado = await _handler.Handle(
            new FinalizarReparoCommand(ordem.Id, Mecanico, "Serviço concluído sem intercorrências"), CancellationToken.None);

        resultado.Status.Should().Be("Finalizada");
        resultado.Resumo.Should().Be("Serviço concluído sem intercorrências");
        _publisherMock.Verify(
            p => p.PublishAsync(
                It.Is<ExecucaoFinalizada>(e =>
                    e.OrdemDeServicoId == ordem.Id
                    && e.Resumo == "Serviço concluído sem intercorrências"
                    && e.FinalizadaEm == Agora
                    && e.OcorridoEm == Agora
                    && e.MessageId != Guid.Empty),
                It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_OResumoPublicadoDeveSerOTextoJaNormalizado()
    {
        var ordem = new OrdemDeProducaoBuilder().EmExecucao(Mecanico);
        OrdemExiste(ordem);

        var resultado = await _handler.Handle(
            new FinalizarReparoCommand(ordem.Id, Mecanico, "  Serviço concluído "), CancellationToken.None);

        resultado.Resumo.Should().Be("Serviço concluído");
        _publisherMock.Verify(
            p => p.PublishAsync(It.Is<ExecucaoFinalizada>(e => e.Resumo == "Serviço concluído"), It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_DevePublicarAntesDeSalvar()
    {
        var ordem = new OrdemDeProducaoBuilder().EmExecucao(Mecanico);
        OrdemExiste(ordem);

        await _handler.Handle(new FinalizarReparoCommand(ordem.Id, Mecanico, "Resumo"), CancellationToken.None);

        _chamadas.Should().Equal("atualizar", "publicar", "salvar");
    }

    [Fact]
    public async Task Handle_OrdemInexistente_DeveLancarKeyNotFoundSemPublicarNemSalvar()
    {
        _repositorioMock
            .Setup(r => r.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrdemDeProducao?)null);

        var act = () => _handler.Handle(
            new FinalizarReparoCommand(Guid.NewGuid(), Mecanico, "Resumo"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*não encontrada*");
        _chamadas.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_PorOutroMecanico_DeveLancarDomainExceptionSemPublicarNemSalvar()
    {
        var ordem = new OrdemDeProducaoBuilder().EmExecucao(Mecanico);
        OrdemExiste(ordem);

        var act = () => _handler.Handle(
            new FinalizarReparoCommand(ordem.Id, "outro-mecanico", "Resumo"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*mecânico responsável*");
        _chamadas.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_OrdemForaDeEmExecucao_DeveLancarDomainExceptionSemPublicarNemSalvar()
    {
        var ordem = new OrdemDeProducaoBuilder().AguardandoExecucao();
        OrdemExiste(ordem);

        var act = () => _handler.Handle(
            new FinalizarReparoCommand(ordem.Id, Mecanico, "Resumo"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*finalizar o reparo*");
        _chamadas.Should().BeEmpty();
    }

    private void OrdemExiste(OrdemDeProducao ordem) =>
        _repositorioMock
            .Setup(r => r.ObterPorIdAsync(ordem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ordem);
}
