using FluentAssertions;
using OficinaMecanica.Execucao.Application.Commands.RegistrarApontamento;
using OficinaMecanica.Execucao.Domain.Entities;
using OficinaMecanica.Execucao.Domain.Exceptions;
using OficinaMecanica.Execucao.Domain.Interfaces;
using OficinaMecanica.Execucao.Tests.Common;

namespace OficinaMecanica.Execucao.Application.Tests.Commands;

public class RegistrarApontamentoCommandHandlerTests
{
    private const string Mecanico = "mecanico-01";
    private static readonly DateTimeOffset Agora = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IOrdemDeProducaoRepository> _repositorioMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly RegistrarApontamentoCommandHandler _handler;

    public RegistrarApontamentoCommandHandlerTests()
    {
        _handler = new RegistrarApontamentoCommandHandler(
            _repositorioMock.Object, _unitOfWorkMock.Object, new RelogioFixo(Agora));
    }

    [Fact]
    public async Task Handle_PeloMecanicoResponsavel_DeveRegistrarAtualizarESalvar()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico(Mecanico).EmDiagnostico();
        OrdemDeServicoExiste(ordem);

        var resultado = await _handler.Handle(
            new RegistrarApontamentoCommand(ordem.Id, Mecanico, "Ruído no cubo da roda"), CancellationToken.None);

        var apontamento = resultado.Apontamentos.Should().ContainSingle().Subject;
        apontamento.MecanicoId.Should().Be(Mecanico);
        apontamento.Texto.Should().Be("Ruído no cubo da roda");
        apontamento.Em.Should().Be(Agora);
        _repositorioMock.Verify(r => r.AtualizarAsync(ordem, It.IsAny<CancellationToken>()), Times.Once());
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_OrdemInexistente_DeveLancarKeyNotFound()
    {
        _repositorioMock
            .Setup(r => r.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrdemDeProducao?)null);

        var act = () => _handler.Handle(
            new RegistrarApontamentoCommand(Guid.NewGuid(), Mecanico, "texto"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*não encontrada*");
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_PorOutroMecanico_DeveLancarDomainExceptionSemAtualizarNemSalvar()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico(Mecanico).EmDiagnostico();
        OrdemDeServicoExiste(ordem);

        var act = () => _handler.Handle(
            new RegistrarApontamentoCommand(ordem.Id, "outro-mecanico", "texto"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*mecânico responsável*");
        _repositorioMock.Verify(r => r.AtualizarAsync(It.IsAny<OrdemDeProducao>(), It.IsAny<CancellationToken>()), Times.Never());
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_OrdemForaDeDiagnosticoOuExecucao_DeveLancarDomainException()
    {
        var ordem = new OrdemDeProducaoBuilder().NaFila();
        OrdemDeServicoExiste(ordem);

        var act = () => _handler.Handle(
            new RegistrarApontamentoCommand(ordem.Id, Mecanico, "texto"), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*registrar apontamento*");
    }

    private void OrdemDeServicoExiste(OrdemDeProducao ordem) =>
        _repositorioMock
            .Setup(r => r.ObterPorIdAsync(ordem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ordem);
}
