using FluentAssertions;
using OficinaMecanica.Execucao.Application.Queries.ObterOrdemDeProducao;
using OficinaMecanica.Execucao.Domain.Entities;
using OficinaMecanica.Execucao.Domain.Interfaces;

namespace OficinaMecanica.Execucao.Application.Tests.Queries;

public class ObterOrdemDeProducaoPorIdQueryHandlerTests
{
    private readonly Mock<IOrdemDeProducaoRepository> _repositorioMock = new();
    private readonly ObterOrdemDeProducaoPorIdQueryHandler _handler;

    public ObterOrdemDeProducaoPorIdQueryHandlerTests()
    {
        _handler = new ObterOrdemDeProducaoPorIdQueryHandler(_repositorioMock.Object);
    }

    [Fact]
    public async Task Handle_OrdemExistente_DeveRetornarODto()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico("mecanico-01").EmDiagnostico();
        _repositorioMock
            .Setup(r => r.ObterPorIdAsync(ordem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ordem);

        var resultado = await _handler.Handle(new ObterOrdemDeProducaoPorIdQuery(ordem.Id), CancellationToken.None);

        resultado.Should().NotBeNull();
        resultado!.Id.Should().Be(ordem.Id);
        resultado.Numero.Should().Be(ordem.Numero);
        resultado.Status.Should().Be("EmDiagnostico");
        resultado.MecanicoId.Should().Be("mecanico-01");
        resultado.Veiculo.Placa.Should().Be(ordem.Veiculo.Placa);
    }

    [Fact]
    public async Task Handle_OrdemInexistente_DeveRetornarNulo()
    {
        _repositorioMock
            .Setup(r => r.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrdemDeProducao?)null);

        var resultado = await _handler.Handle(new ObterOrdemDeProducaoPorIdQuery(Guid.NewGuid()), CancellationToken.None);

        resultado.Should().BeNull();
    }
}
