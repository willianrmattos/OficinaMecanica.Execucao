using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OficinaMecanica.Contracts.Events;
using OficinaMecanica.Execucao.Application.Commands.ReivindicarProximoDiagnostico;
using OficinaMecanica.Execucao.Application.Interfaces;
using OficinaMecanica.Execucao.Domain.Entities;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Domain.Exceptions;
using OficinaMecanica.Execucao.Domain.Interfaces;
using OficinaMecanica.Execucao.Tests.Common;

namespace OficinaMecanica.Execucao.Application.Tests.Commands;

public class ReivindicarProximoDiagnosticoCommandHandlerTests
{
    private const string Mecanico = "mecanico-01";
    private static readonly DateTimeOffset Agora = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IOrdemDeProducaoRepository> _repositorioMock = new();
    private readonly Mock<IIntegrationEventPublisher> _publisherMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly List<string> _chamadas = new();
    private readonly ReivindicarProximoDiagnosticoCommandHandler _handler;

    public ReivindicarProximoDiagnosticoCommandHandlerTests()
    {
        _repositorioMock
            .Setup(r => r.AtualizarAsync(It.IsAny<OrdemDeProducao>(), It.IsAny<CancellationToken>()))
            .Callback(() => _chamadas.Add("atualizar"))
            .Returns(Task.CompletedTask);
        _publisherMock
            .Setup(p => p.PublishAsync(It.IsAny<DiagnosticoIniciado>(), It.IsAny<CancellationToken>()))
            .Callback(() => _chamadas.Add("publicar"))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => _chamadas.Add("salvar"))
            .Returns(Task.CompletedTask);

        _handler = new ReivindicarProximoDiagnosticoCommandHandler(
            _repositorioMock.Object,
            _publisherMock.Object,
            _unitOfWorkMock.Object,
            new RelogioFixo(Agora),
            NullLogger<ReivindicarProximoDiagnosticoCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ComOrdemNaFila_DeveReivindicarEPublicarDiagnosticoIniciado()
    {
        var ordem = new OrdemDeProducaoBuilder().NaFila();
        FilaRetorna(ordem);

        var resultado = await _handler.Handle(new ReivindicarProximoDiagnosticoCommand(Mecanico), CancellationToken.None);

        resultado.FilaVazia.Should().BeFalse();
        resultado.Ordem.Should().NotBeNull();
        resultado.Ordem!.Id.Should().Be(ordem.Id);
        resultado.Ordem.Status.Should().Be("EmDiagnostico");
        resultado.Ordem.MecanicoId.Should().Be(Mecanico);
        ordem.Status.Should().Be(StatusProducao.EmDiagnostico);
        ordem.Etapas.Single().Inicio.Should().Be(Agora);
        _publisherMock.Verify(
            p => p.PublishAsync(
                It.Is<DiagnosticoIniciado>(e =>
                    e.OrdemDeServicoId == ordem.Id
                    && e.MecanicoId == Mecanico
                    && e.OcorridoEm == Agora
                    && e.MessageId != Guid.Empty),
                It.IsAny<CancellationToken>()),
            Times.Once());
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_DeveBuscarSomenteNaFilaDeDiagnostico()
    {
        FilaRetorna(new OrdemDeProducaoBuilder().NaFila());

        await _handler.Handle(new ReivindicarProximoDiagnosticoCommand(Mecanico), CancellationToken.None);

        _repositorioMock.Verify(
            r => r.ObterProximaDaFilaAsync(StatusProducao.Fila, It.IsAny<CancellationToken>()), Times.Once());
        _repositorioMock.Verify(
            r => r.ObterProximaDaFilaAsync(It.Is<StatusProducao>(s => s != StatusProducao.Fila), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task Handle_DevePublicarAntesDeSalvar()
    {
        FilaRetorna(new OrdemDeProducaoBuilder().NaFila());

        await _handler.Handle(new ReivindicarProximoDiagnosticoCommand(Mecanico), CancellationToken.None);

        _chamadas.Should().Equal("atualizar", "publicar", "salvar");
    }

    [Fact]
    public async Task Handle_FilaVazia_DeveRetornarFilaVaziaSemPublicarNemSalvar()
    {
        FilaRetorna((OrdemDeProducao?)null);

        var resultado = await _handler.Handle(new ReivindicarProximoDiagnosticoCommand(Mecanico), CancellationToken.None);

        resultado.FilaVazia.Should().BeTrue();
        resultado.Ordem.Should().BeNull();
        _chamadas.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ConflitoDeConcorrenciaNaPrimeiraTentativa_DeveTentarAProximaDaFila()
    {
        var disputada = new OrdemDeProducaoBuilder().NaFila();
        var proxima = new OrdemDeProducaoBuilder().NaFila();
        FilaRetorna(disputada, proxima);
        _repositorioMock
            .SetupSequence(r => r.AtualizarAsync(It.IsAny<OrdemDeProducao>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcorrenciaException("conflito"))
            .Returns(Task.CompletedTask);

        var resultado = await _handler.Handle(new ReivindicarProximoDiagnosticoCommand(Mecanico), CancellationToken.None);

        resultado.Ordem!.Id.Should().Be(proxima.Id);
        _repositorioMock.Verify(r => r.ObterProximaDaFilaAsync(StatusProducao.Fila, It.IsAny<CancellationToken>()), Times.Exactly(2));
        _repositorioMock.Verify(r => r.AtualizarAsync(It.IsAny<OrdemDeProducao>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _publisherMock.Verify(
            p => p.PublishAsync(It.IsAny<DiagnosticoIniciado>(), It.IsAny<CancellationToken>()), Times.Once());
        _publisherMock.Verify(
            p => p.PublishAsync(It.Is<DiagnosticoIniciado>(e => e.OrdemDeServicoId == proxima.Id), It.IsAny<CancellationToken>()),
            Times.Once());
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_ConflitoQuandoOutroMecanicoLevouAUltimaOrdem_DeveRetornarFilaVazia()
    {
        FilaRetorna(new OrdemDeProducaoBuilder().NaFila(), null);
        _repositorioMock
            .SetupSequence(r => r.AtualizarAsync(It.IsAny<OrdemDeProducao>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcorrenciaException("conflito"));

        var resultado = await _handler.Handle(new ReivindicarProximoDiagnosticoCommand(Mecanico), CancellationToken.None);

        resultado.FilaVazia.Should().BeTrue();
        _publisherMock.Verify(p => p.PublishAsync(It.IsAny<DiagnosticoIniciado>(), It.IsAny<CancellationToken>()), Times.Never());
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_ConflitoEmTodasAsTentativas_DevePropagarAConcorrenciaExceptionSemPublicarNemSalvar()
    {
        FilaRetorna(
            new OrdemDeProducaoBuilder().NaFila(),
            new OrdemDeProducaoBuilder().NaFila(),
            new OrdemDeProducaoBuilder().NaFila(),
            new OrdemDeProducaoBuilder().NaFila(),
            new OrdemDeProducaoBuilder().NaFila(),
            new OrdemDeProducaoBuilder().NaFila());
        _repositorioMock
            .Setup(r => r.AtualizarAsync(It.IsAny<OrdemDeProducao>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcorrenciaException("conflito"));

        var act = () => _handler.Handle(new ReivindicarProximoDiagnosticoCommand(Mecanico), CancellationToken.None);

        await act.Should().ThrowAsync<ConcorrenciaException>();
        _repositorioMock.Verify(r => r.ObterProximaDaFilaAsync(StatusProducao.Fila, It.IsAny<CancellationToken>()), Times.Exactly(5));
        _publisherMock.Verify(p => p.PublishAsync(It.IsAny<DiagnosticoIniciado>(), It.IsAny<CancellationToken>()), Times.Never());
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_SemMecanico_DeveLancarDomainExceptionSemAtualizarPublicarNemSalvar()
    {
        FilaRetorna(new OrdemDeProducaoBuilder().NaFila());

        var act = () => _handler.Handle(new ReivindicarProximoDiagnosticoCommand(""), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*mecânico é obrigatório*");
        _chamadas.Should().BeEmpty();
    }

    private void FilaRetorna(params OrdemDeProducao?[] ordens)
    {
        var sequencia = _repositorioMock
            .SetupSequence(r => r.ObterProximaDaFilaAsync(StatusProducao.Fila, It.IsAny<CancellationToken>()));

        foreach (var ordem in ordens)
            sequencia.ReturnsAsync(ordem);
    }
}
