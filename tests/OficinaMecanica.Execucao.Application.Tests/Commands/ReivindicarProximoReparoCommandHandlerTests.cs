using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OficinaMecanica.Execucao.Application.Commands.ReivindicarProximoReparo;
using OficinaMecanica.Execucao.Domain.Entities;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Domain.Exceptions;
using OficinaMecanica.Execucao.Domain.Interfaces;
using OficinaMecanica.Execucao.Tests.Common;

namespace OficinaMecanica.Execucao.Application.Tests.Commands;

public class ReivindicarProximoReparoCommandHandlerTests
{
    private const string Mecanico = "reparador-01";
    private static readonly DateTimeOffset Agora = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IOrdemDeProducaoRepository> _repositorioMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly List<string> _chamadas = new();
    private readonly ReivindicarProximoReparoCommandHandler _handler;

    public ReivindicarProximoReparoCommandHandlerTests()
    {
        _repositorioMock
            .Setup(r => r.AtualizarAsync(It.IsAny<OrdemDeProducao>(), It.IsAny<CancellationToken>()))
            .Callback(() => _chamadas.Add("atualizar"))
            .Returns(Task.CompletedTask);
        _unitOfWorkMock
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => _chamadas.Add("salvar"))
            .Returns(Task.CompletedTask);

        _handler = new ReivindicarProximoReparoCommandHandler(
            _repositorioMock.Object,
            _unitOfWorkMock.Object,
            new RelogioFixo(Agora),
            NullLogger<ReivindicarProximoReparoCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ComOrdemAguardandoExecucao_DeveReivindicarEIniciarOReparo()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico("diagnosticador").AguardandoExecucao();
        FilaRetorna(ordem);

        var resultado = await _handler.Handle(new ReivindicarProximoReparoCommand(Mecanico), CancellationToken.None);

        resultado.FilaVazia.Should().BeFalse();
        resultado.Ordem!.Id.Should().Be(ordem.Id);
        resultado.Ordem.Status.Should().Be("EmExecucao");
        resultado.Ordem.MecanicoId.Should().Be(Mecanico);
        ordem.Status.Should().Be(StatusProducao.EmExecucao);
        ordem.Etapas.Last().Inicio.Should().Be(Agora);
        _chamadas.Should().Equal("atualizar", "salvar");
    }

    [Fact]
    public async Task Handle_DeveBuscarSomenteNaFilaDeExecucao()
    {
        FilaRetorna(new OrdemDeProducaoBuilder().AguardandoExecucao());

        await _handler.Handle(new ReivindicarProximoReparoCommand(Mecanico), CancellationToken.None);

        _repositorioMock.Verify(
            r => r.ObterProximaDaFilaAsync(StatusProducao.AguardandoExecucao, It.IsAny<CancellationToken>()), Times.Once());
        _repositorioMock.Verify(
            r => r.ObterProximaDaFilaAsync(It.Is<StatusProducao>(s => s != StatusProducao.AguardandoExecucao), It.IsAny<CancellationToken>()),
            Times.Never());
    }

    [Fact]
    public async Task Handle_FilaVazia_DeveRetornarFilaVaziaSemSalvar()
    {
        FilaRetorna((OrdemDeProducao?)null);

        var resultado = await _handler.Handle(new ReivindicarProximoReparoCommand(Mecanico), CancellationToken.None);

        resultado.FilaVazia.Should().BeTrue();
        resultado.Ordem.Should().BeNull();
        _chamadas.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ConflitoDeConcorrenciaNaPrimeiraTentativa_DeveTentarAProximaDaFila()
    {
        var disputada = new OrdemDeProducaoBuilder().AguardandoExecucao();
        var proxima = new OrdemDeProducaoBuilder().AguardandoExecucao();
        FilaRetorna(disputada, proxima);
        _repositorioMock
            .SetupSequence(r => r.AtualizarAsync(It.IsAny<OrdemDeProducao>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcorrenciaException("conflito"))
            .Returns(Task.CompletedTask);

        var resultado = await _handler.Handle(new ReivindicarProximoReparoCommand(Mecanico), CancellationToken.None);

        resultado.Ordem!.Id.Should().Be(proxima.Id);
        _repositorioMock.Verify(
            r => r.ObterProximaDaFilaAsync(StatusProducao.AguardandoExecucao, It.IsAny<CancellationToken>()), Times.Exactly(2));
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_ConflitoEmTodasAsTentativas_DevePropagarAConcorrenciaExceptionSemSalvar()
    {
        FilaRetorna(
            new OrdemDeProducaoBuilder().AguardandoExecucao(),
            new OrdemDeProducaoBuilder().AguardandoExecucao(),
            new OrdemDeProducaoBuilder().AguardandoExecucao(),
            new OrdemDeProducaoBuilder().AguardandoExecucao(),
            new OrdemDeProducaoBuilder().AguardandoExecucao(),
            new OrdemDeProducaoBuilder().AguardandoExecucao());
        _repositorioMock
            .Setup(r => r.AtualizarAsync(It.IsAny<OrdemDeProducao>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcorrenciaException("conflito"));

        var act = () => _handler.Handle(new ReivindicarProximoReparoCommand(Mecanico), CancellationToken.None);

        await act.Should().ThrowAsync<ConcorrenciaException>();
        _repositorioMock.Verify(
            r => r.ObterProximaDaFilaAsync(StatusProducao.AguardandoExecucao, It.IsAny<CancellationToken>()), Times.Exactly(5));
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_SemMecanico_DeveLancarDomainExceptionSemAtualizarNemSalvar()
    {
        FilaRetorna(new OrdemDeProducaoBuilder().AguardandoExecucao());

        var act = () => _handler.Handle(new ReivindicarProximoReparoCommand(" "), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*mecânico é obrigatório*");
        _chamadas.Should().BeEmpty();
    }

    private void FilaRetorna(params OrdemDeProducao?[] ordens)
    {
        var sequencia = _repositorioMock
            .SetupSequence(r => r.ObterProximaDaFilaAsync(StatusProducao.AguardandoExecucao, It.IsAny<CancellationToken>()));

        foreach (var ordem in ordens)
            sequencia.ReturnsAsync(ordem);
    }
}
