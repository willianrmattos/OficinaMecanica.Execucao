using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OficinaMecanica.Execucao.Application.Commands.CancelarProducao;
using OficinaMecanica.Execucao.Domain.Entities;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Domain.Exceptions;
using OficinaMecanica.Execucao.Domain.Interfaces;
using OficinaMecanica.Execucao.Tests.Common;

namespace OficinaMecanica.Execucao.Application.Tests.Commands;

public class CancelarProducaoCommandHandlerTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IOrdemDeProducaoRepository> _repositorioMock = new();
    private readonly Mock<ICancelamentoPrevioRepository> _cancelamentoPrevioRepositorioMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly CancelarProducaoCommandHandler _handler;

    public CancelarProducaoCommandHandlerTests()
    {
        _repositorioMock
            .Setup(r => r.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrdemDeProducao?)null);
        _cancelamentoPrevioRepositorioMock
            .Setup(r => r.ObterAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CancelamentoPrevio?)null);
        _cancelamentoPrevioRepositorioMock
            .Setup(r => r.AdicionarAsync(It.IsAny<CancelamentoPrevio>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _handler = new CancelarProducaoCommandHandler(
            _repositorioMock.Object,
            _cancelamentoPrevioRepositorioMock.Object,
            _unitOfWorkMock.Object,
            new RelogioFixo(Agora),
            NullLogger<CancelarProducaoCommandHandler>.Instance);
    }

    [Theory]
    [InlineData(StatusProducao.Fila)]
    [InlineData(StatusProducao.EmDiagnostico)]
    [InlineData(StatusProducao.DiagnosticoConcluido)]
    [InlineData(StatusProducao.AguardandoExecucao)]
    public async Task Handle_EmStatusCancelavel_DeveCancelarAtualizarESalvar(StatusProducao status)
    {
        var ordem = new OrdemDeProducaoBuilder().NoStatus(status);
        OrdemExiste(ordem);

        await _handler.Handle(new CancelarProducaoCommand(ordem.Id, "Pagamento cancelado"), CancellationToken.None);

        ordem.Status.Should().Be(StatusProducao.Cancelada);
        ordem.MotivoCancelamento.Should().Be("Pagamento cancelado");
        _repositorioMock.Verify(r => r.AtualizarAsync(ordem, It.IsAny<CancellationToken>()), Times.Once());
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once());
        _cancelamentoPrevioRepositorioMock.Verify(r => r.AdicionarAsync(It.IsAny<CancelamentoPrevio>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_OrdemInexistente_DeveRegistrarCancelamentoPrevioESalvar()
    {
        var ordemDeServicoId = Guid.NewGuid();
        CancelamentoPrevio? registrado = null;
        _cancelamentoPrevioRepositorioMock
            .Setup(r => r.AdicionarAsync(It.IsAny<CancelamentoPrevio>(), It.IsAny<CancellationToken>()))
            .Callback<CancelamentoPrevio, CancellationToken>((cancelamento, _) => registrado = cancelamento)
            .ReturnsAsync(true);

        await _handler.Handle(new CancelarProducaoCommand(ordemDeServicoId, "Pagamento cancelado"), CancellationToken.None);

        registrado.Should().NotBeNull();
        registrado!.OrdemDeServicoId.Should().Be(ordemDeServicoId);
        registrado.Motivo.Should().Be("Pagamento cancelado");
        registrado.RegistradoEm.Should().Be(Agora);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once());
        _repositorioMock.Verify(r => r.AtualizarAsync(It.IsAny<OrdemDeProducao>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_OrdemInexistenteComCancelamentoPrevioJaRegistrado_DeveSerIdempotenteSemAdicionarNemSalvar()
    {
        var tombstone = new CancelamentoPrevioBuilder().Build();
        _cancelamentoPrevioRepositorioMock
            .Setup(r => r.ObterAsync(tombstone.OrdemDeServicoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tombstone);

        var act = () => _handler.Handle(new CancelarProducaoCommand(tombstone.OrdemDeServicoId, "Reentrega"), CancellationToken.None);

        await act.Should().NotThrowAsync();
        _cancelamentoPrevioRepositorioMock.Verify(r => r.AdicionarAsync(It.IsAny<CancelamentoPrevio>(), It.IsAny<CancellationToken>()), Times.Never());
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_EntregaConcorrenteVenceORegistroDoCancelamentoPrevio_DeveDescartarSemSalvar()
    {
        _cancelamentoPrevioRepositorioMock
            .Setup(r => r.AdicionarAsync(It.IsAny<CancelamentoPrevio>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var act = () => _handler.Handle(new CancelarProducaoCommand(Guid.NewGuid(), "Pagamento cancelado"), CancellationToken.None);

        await act.Should().NotThrowAsync();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_Reentrega_DeveSerIdempotenteSemAtualizarNemSalvar()
    {
        var ordem = new OrdemDeProducaoBuilder().Cancelada("Motivo original");
        OrdemExiste(ordem);

        var act = () => _handler.Handle(new CancelarProducaoCommand(ordem.Id, "Reentrega"), CancellationToken.None);

        await act.Should().NotThrowAsync();
        ordem.MotivoCancelamento.Should().Be("Motivo original");
        _repositorioMock.Verify(r => r.AtualizarAsync(It.IsAny<OrdemDeProducao>(), It.IsAny<CancellationToken>()), Times.Never());
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    [Theory]
    [InlineData(StatusProducao.EmExecucao)]
    [InlineData(StatusProducao.Finalizada)]
    [InlineData(StatusProducao.Inviavel)]
    public async Task Handle_EmStatusNaoCancelavel_DeveLancarDomainExceptionSemSalvar(StatusProducao status)
    {
        var ordem = new OrdemDeProducaoBuilder().NoStatus(status);
        OrdemExiste(ordem);

        var act = () => _handler.Handle(new CancelarProducaoCommand(ordem.Id, "Pagamento cancelado"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*cancelar a produção*");
        ordem.Status.Should().Be(status);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    private void OrdemExiste(OrdemDeProducao ordem) =>
        _repositorioMock
            .Setup(r => r.ObterPorIdAsync(ordem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ordem);
}
