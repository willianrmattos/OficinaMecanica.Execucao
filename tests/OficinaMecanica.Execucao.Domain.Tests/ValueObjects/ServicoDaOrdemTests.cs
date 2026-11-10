using FluentAssertions;
using OficinaMecanica.Execucao.Domain.Exceptions;
using OficinaMecanica.Execucao.Domain.ValueObjects;

namespace OficinaMecanica.Execucao.Domain.Tests.ValueObjects;

public class ServicoDaOrdemTests
{
    [Fact]
    public void Criar_ComDadosValidos_DeveGuardarOsValores()
    {
        var servicoId = Guid.NewGuid();

        var servico = ServicoDaOrdem.Criar(servicoId, "Troca de óleo", 2, 120.50m);

        servico.ServicoId.Should().Be(servicoId);
        servico.Nome.Should().Be("Troca de óleo");
        servico.Quantidade.Should().Be(2);
        servico.PrecoUnitario.Should().Be(120.50m);
    }

    [Fact]
    public void Criar_ComPrecoZero_DeveAceitar()
    {
        var servico = new ServicoDaOrdemBuilder().ComPrecoUnitario(0m).Build();

        servico.PrecoUnitario.Should().Be(0m);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Criar_SemNome_DeveLancarExcecao(string nome)
    {
        var act = () => new ServicoDaOrdemBuilder().ComNome(nome).Build();

        act.Should().Throw<DomainException>().WithMessage("*nome do serviço*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Criar_ComQuantidadeNaoPositiva_DeveLancarExcecao(int quantidade)
    {
        var act = () => new ServicoDaOrdemBuilder().ComQuantidade(quantidade).Build();

        act.Should().Throw<DomainException>().WithMessage("*quantidade*maior que zero*");
    }

    [Fact]
    public void Criar_ComPrecoNegativo_DeveLancarExcecao()
    {
        var act = () => new ServicoDaOrdemBuilder().ComPrecoUnitario(-0.01m).Build();

        act.Should().Throw<DomainException>().WithMessage("*preço*negativo*");
    }

    [Fact]
    public void Igualdade_ComMesmosValores_DeveSerIgual()
    {
        var servicoId = Guid.NewGuid();
        var servico = new ServicoDaOrdemBuilder().ComServicoId(servicoId).ComNome("Alinhamento").ComQuantidade(1).ComPrecoUnitario(80m).Build();
        var outro = new ServicoDaOrdemBuilder().ComServicoId(servicoId).ComNome("Alinhamento").ComQuantidade(1).ComPrecoUnitario(80m).Build();

        servico.Should().Be(outro);
        servico.GetHashCode().Should().Be(outro.GetHashCode());
    }

    [Fact]
    public void Igualdade_ComQuantidadeDiferente_DeveSerDiferente()
    {
        var servicoId = Guid.NewGuid();
        var servico = new ServicoDaOrdemBuilder().ComServicoId(servicoId).ComQuantidade(1).Build();
        var outro = new ServicoDaOrdemBuilder().ComServicoId(servicoId).ComQuantidade(2).Build();

        servico.Should().NotBe(outro);
    }
}
