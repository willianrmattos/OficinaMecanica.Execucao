using FluentAssertions;
using OficinaMecanica.Contracts.Events;
using OficinaMecanica.Execucao.Application.Commands.InviabilizarExecucao;
using OficinaMecanica.Execucao.Application.Interfaces;
using OficinaMecanica.Execucao.Domain.Entities;
using OficinaMecanica.Execucao.Domain.Exceptions;
using OficinaMecanica.Execucao.Domain.Interfaces;
using OficinaMecanica.Execucao.Tests.Common;

namespace OficinaMecanica.Execucao.Application.Tests.Commands;

public class InviabilizarExecucaoCommandHandlerTests
{
    private const string Mecanico = "reparador-01";
    private static readonly DateTimeOffset Agora = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IOrdemDeProducaoRepository> _repositorioMock = new();
    private readonly Mock<IIntegrationEventPublisher> _publisherMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly List<string> _chamadas = new();
    private readonly InviabilizarExecucaoCommandHandler _handler;

    public InviabilizarExecucaoCommandHandlerTests()
    {
        _repositorioMock
            .Setup(r => r.AtualizarAsync(It.IsAny<OrdemDeProducao>(), It.IsAny<CancellationToken>()))
            .Callback(() => _chamadas.Add("atualizar"))
            .Returns(Task.CompletedTask);
        _publisherMock
            .Setup(p => p.PublishAsync(It.IsAny<ExecucaoInviavel>(), It.IsAny<CancellationToken>()))
            .Callback(() => _chamadas.Add("publicar"))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => _chamadas.Add("salvar"))
            .Returns(Task.CompletedTask);

        _handler = new InviabilizarExecucaoCommandHandler(
            _repositorioMock.Object, _publisherMock.Object, _unitOfWorkMock.Object, new RelogioFixo(Agora));
    }

    [Fact]
    public async Task Handle_OrdemEmExecucaoPeloResponsavel_DeveInviabilizarEPublicarExecucaoInviavel()
    {
        var ordem = new OrdemDeProducaoBuilder().EmExecucao(Mecanico);
        OrdemExiste(ordem);

        var resultado = await _handler.Handle(
            new InviabilizarExecucaoCommand(ordem.Id, Mecanico, "Peça indisponível no fornecedor"), CancellationToken.None);

        resultado.Status.Should().Be("Inviavel");
        resultado.MotivoInviabilidade.Should().Be("Peça indisponível no fornecedor");
        resultado.InviabilizadaPor.Should().Be(Mecanico);
        resultado.Etapas.Last().Status.Should().Be("Interrompida");
        _publisherMock.Verify(
            p => p.PublishAsync(
                It.Is<ExecucaoInviavel>(e =>
                    e.OrdemDeServicoId == ordem.Id
                    && e.Motivo == "Peça indisponível no fornecedor"
                    && e.OcorridoEm == Agora
                    && e.MessageId != Guid.Empty),
                It.IsAny<CancellationToken>()),
            Times.Once());
    }

    [Fact]
    public async Task Handle_OrdemAguardandoExecucaoPorQualquerMecanico_DeveInviabilizarEPublicar()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico("diagnosticador").AguardandoExecucao();
        OrdemExiste(ordem);

        var resultado = await _handler.Handle(
            new InviabilizarExecucaoCommand(ordem.Id, "supervisor-01", "Fornecedor descontinuou a peça"), CancellationToken.None);

        resultado.Status.Should().Be("Inviavel");
        resultado.InviabilizadaPor.Should().Be("supervisor-01");
        _publisherMock.Verify(p => p.PublishAsync(It.IsAny<ExecucaoInviavel>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_OMotivoPublicadoDeveSerOTextoJaNormalizado()
    {
        var ordem = new OrdemDeProducaoBuilder().EmExecucao(Mecanico);
        OrdemExiste(ordem);

        await _handler.Handle(new InviabilizarExecucaoCommand(ordem.Id, Mecanico, "  Peça indisponível "), CancellationToken.None);

        _publisherMock.Verify(
            p => p.PublishAsync(It.Is<ExecucaoInviavel>(e => e.Motivo == "Peça indisponível"), It.IsAny<CancellationToken>()),
            Times.Once());
        ordem.MotivoInviabilidade.Should().Be("Peça indisponível");
    }

    [Fact]
    public async Task Handle_DevePublicarAntesDeSalvar()
    {
        var ordem = new OrdemDeProducaoBuilder().EmExecucao(Mecanico);
        OrdemExiste(ordem);

        await _handler.Handle(new InviabilizarExecucaoCommand(ordem.Id, Mecanico, "Motivo"), CancellationToken.None);

        _chamadas.Should().Equal("atualizar", "publicar", "salvar");
    }

    [Fact]
    public async Task Handle_OrdemInexistente_DeveLancarKeyNotFoundSemPublicarNemSalvar()
    {
        _repositorioMock
            .Setup(r => r.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrdemDeProducao?)null);

        var act = () => _handler.Handle(new InviabilizarExecucaoCommand(Guid.NewGuid(), Mecanico, "Motivo"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*não encontrada*");
        _chamadas.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_EmExecucaoPorOutroMecanico_DeveLancarDomainExceptionSemPublicarNemSalvar()
    {
        var ordem = new OrdemDeProducaoBuilder().EmExecucao(Mecanico);
        OrdemExiste(ordem);

        var act = () => _handler.Handle(new InviabilizarExecucaoCommand(ordem.Id, "outro-mecanico", "Motivo"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*mecânico responsável*");
        _chamadas.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_OrdemJaFinalizada_DeveLancarDomainExceptionSemPublicarNemSalvar()
    {
        var ordem = new OrdemDeProducaoBuilder().Finalizada();
        OrdemExiste(ordem);

        var act = () => _handler.Handle(new InviabilizarExecucaoCommand(ordem.Id, Mecanico, "Motivo"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*inviabilizar a execução*");
        _chamadas.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SemMotivo_DeveLancarDomainExceptionSemPublicarNemSalvar()
    {
        var ordem = new OrdemDeProducaoBuilder().EmExecucao(Mecanico);
        OrdemExiste(ordem);

        var act = () => _handler.Handle(new InviabilizarExecucaoCommand(ordem.Id, Mecanico, " "), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*motivo da inviabilidade*");
        _chamadas.Should().BeEmpty();
    }

    private void OrdemExiste(OrdemDeProducao ordem) =>
        _repositorioMock
            .Setup(r => r.ObterPorIdAsync(ordem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ordem);
}
