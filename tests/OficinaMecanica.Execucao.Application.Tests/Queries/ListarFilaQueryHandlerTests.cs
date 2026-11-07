using FluentAssertions;
using OficinaMecanica.Execucao.Application.Queries.ListarFila;
using OficinaMecanica.Execucao.Domain.Entities;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Domain.Interfaces;

namespace OficinaMecanica.Execucao.Application.Tests.Queries;

public class ListarFilaQueryHandlerTests
{
    private readonly Mock<IOrdemDeProducaoRepository> _repositorioMock = new();
    private readonly ListarFilaQueryHandler _handler;

    public ListarFilaQueryHandlerTests()
    {
        _handler = new ListarFilaQueryHandler(_repositorioMock.Object);
    }

    [Fact]
    public async Task Handle_DeveRepassarOsFiltrosEMontarAPaginacao()
    {
        var ordens = new List<OrdemDeProducao>
        {
            new OrdemDeProducaoBuilder().NaFila(),
            new OrdemDeProducaoBuilder().NaFila()
        };
        _repositorioMock
            .Setup(r => r.ListarAsync(StatusProducao.Fila, "mecanico-01", 2, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ordens);
        _repositorioMock
            .Setup(r => r.ContarAsync(StatusProducao.Fila, "mecanico-01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(25);

        var resultado = await _handler.Handle(
            new ListarFilaQuery(StatusProducao.Fila, "mecanico-01", 2, 10), CancellationToken.None);

        resultado.Total.Should().Be(25);
        resultado.Pagina.Should().Be(2);
        resultado.TamanhoPagina.Should().Be(10);
        resultado.TotalPaginas.Should().Be(3);
        resultado.Itens.Select(i => i.Id).Should().Equal(ordens.Select(o => o.Id));
    }

    [Fact]
    public async Task Handle_DeveMapearOResumoDeCadaOrdem()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico("mecanico-01").EmDiagnostico();
        _repositorioMock
            .Setup(r => r.ListarAsync(null, null, 1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrdemDeProducao> { ordem });
        _repositorioMock
            .Setup(r => r.ContarAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var resultado = await _handler.Handle(new ListarFilaQuery(), CancellationToken.None);

        var item = resultado.Itens.Should().ContainSingle().Subject;
        item.Numero.Should().Be(ordem.Numero);
        item.Placa.Should().Be(ordem.Veiculo.Placa);
        item.Marca.Should().Be(ordem.Veiculo.Marca);
        item.Modelo.Should().Be(ordem.Veiculo.Modelo);
        item.Status.Should().Be("EmDiagnostico");
        item.MecanicoId.Should().Be("mecanico-01");
        item.EnfileiradaEm.Should().Be(ordem.EnfileiradaEm);
    }

    [Fact]
    public async Task Handle_SemResultados_DeveRetornarPaginaVazia()
    {
        _repositorioMock
            .Setup(r => r.ListarAsync(It.IsAny<StatusProducao?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OrdemDeProducao>());
        _repositorioMock
            .Setup(r => r.ContarAsync(It.IsAny<StatusProducao?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var resultado = await _handler.Handle(new ListarFilaQuery(), CancellationToken.None);

        resultado.Itens.Should().BeEmpty();
        resultado.Total.Should().Be(0);
        resultado.TotalPaginas.Should().Be(0);
    }
}
