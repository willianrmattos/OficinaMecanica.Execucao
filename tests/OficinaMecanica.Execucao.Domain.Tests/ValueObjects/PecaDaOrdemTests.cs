using FluentAssertions;
using OficinaMecanica.Execucao.Domain.Exceptions;
using OficinaMecanica.Execucao.Domain.ValueObjects;

namespace OficinaMecanica.Execucao.Domain.Tests.ValueObjects;

public class PecaDaOrdemTests
{
    [Fact]
    public void Criar_ComDadosValidos_DeveGuardarOsValores()
    {
        var pecaId = Guid.NewGuid();

        var peca = PecaDaOrdem.Criar(pecaId, "Filtro de óleo", 2, 35.90m);

        peca.PecaId.Should().Be(pecaId);
        peca.Nome.Should().Be("Filtro de óleo");
        peca.Quantidade.Should().Be(2);
        peca.PrecoUnitario.Should().Be(35.90m);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Criar_SemNome_DeveLancarExcecao(string nome)
    {
        var act = () => new PecaDaOrdemBuilder().ComNome(nome).Build();

        act.Should().Throw<DomainException>().WithMessage("*nome da peça*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Criar_ComQuantidadeNaoPositiva_DeveLancarExcecao(int quantidade)
    {
        var act = () => new PecaDaOrdemBuilder().ComQuantidade(quantidade).Build();

        act.Should().Throw<DomainException>().WithMessage("*quantidade*maior que zero*");
    }

    [Fact]
    public void Criar_ComPrecoNegativo_DeveLancarExcecao()
    {
        var act = () => new PecaDaOrdemBuilder().ComPrecoUnitario(-1m).Build();

        act.Should().Throw<DomainException>().WithMessage("*preço*negativo*");
    }

    [Fact]
    public void Igualdade_ComMesmosValores_DeveSerIgual()
    {
        var pecaId = Guid.NewGuid();
        var peca = new PecaDaOrdemBuilder().ComPecaId(pecaId).ComNome("Vela").ComQuantidade(4).ComPrecoUnitario(25m).Build();
        var outra = new PecaDaOrdemBuilder().ComPecaId(pecaId).ComNome("Vela").ComQuantidade(4).ComPrecoUnitario(25m).Build();

        peca.Should().Be(outra);
        peca.GetHashCode().Should().Be(outra.GetHashCode());
    }
}
