using FluentAssertions;
using OficinaMecanica.Execucao.Application.Queries.ObterResumoDaFila;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Domain.Interfaces;

namespace OficinaMecanica.Execucao.Application.Tests.Queries;

public class ObterResumoDaFilaQueryHandlerTests
{
    private readonly Mock<IOrdemDeProducaoRepository> _repositorioMock = new();
    private readonly ObterResumoDaFilaQueryHandler _handler;

    public ObterResumoDaFilaQueryHandlerTests()
    {
        _handler = new ObterResumoDaFilaQueryHandler(_repositorioMock.Object);
    }

    [Fact]
    public async Task Handle_DeveRetornarContagemPorStatusComZeroParaOsAusentes()
    {
        _repositorioMock
            .Setup(r => r.ContarPorStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<StatusProducao, int>
            {
                [StatusProducao.Fila] = 4,
                [StatusProducao.EmExecucao] = 2
            });

        var resultado = await _handler.Handle(new ObterResumoDaFilaQuery(), CancellationToken.None);

        resultado.PorStatus["Fila"].Should().Be(4);
        resultado.PorStatus["EmExecucao"].Should().Be(2);
        resultado.PorStatus["Cancelada"].Should().Be(0);
        resultado.PorStatus.Keys.Should().BeEquivalentTo(Enum.GetNames<StatusProducao>());
        resultado.Total.Should().Be(6);
    }

    [Fact]
    public async Task Handle_SemOrdens_DeveRetornarTudoZerado()
    {
        _repositorioMock
            .Setup(r => r.ContarPorStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<StatusProducao, int>());

        var resultado = await _handler.Handle(new ObterResumoDaFilaQuery(), CancellationToken.None);

        resultado.PorStatus.Values.Should().OnlyContain(v => v == 0);
        resultado.Total.Should().Be(0);
    }
}
