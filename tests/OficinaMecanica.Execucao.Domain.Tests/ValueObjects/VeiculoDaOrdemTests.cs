using FluentAssertions;
using OficinaMecanica.Execucao.Domain.Exceptions;
using OficinaMecanica.Execucao.Domain.ValueObjects;

namespace OficinaMecanica.Execucao.Domain.Tests.ValueObjects;

public class VeiculoDaOrdemTests
{
    [Fact]
    public void Criar_ComDadosValidos_DeveGuardarOsValores()
    {
        var veiculo = VeiculoDaOrdem.Criar("ABC1D23", "Fiat", "Uno");

        veiculo.Placa.Should().Be("ABC1D23");
        veiculo.Marca.Should().Be("Fiat");
        veiculo.Modelo.Should().Be("Uno");
    }

    [Fact]
    public void Criar_ComEspacosNasPontas_DeveAparar()
    {
        var veiculo = VeiculoDaOrdem.Criar("  ABC1D23 ", " Fiat ", " Uno  ");

        veiculo.Should().Be(VeiculoDaOrdem.Criar("ABC1D23", "Fiat", "Uno"));
    }

    [Theory]
    [InlineData("", "Fiat", "Uno", "*placa*")]
    [InlineData("  ", "Fiat", "Uno", "*placa*")]
    [InlineData("ABC1D23", "", "Uno", "*marca*")]
    [InlineData("ABC1D23", " ", "Uno", "*marca*")]
    [InlineData("ABC1D23", "Fiat", "", "*modelo*")]
    [InlineData("ABC1D23", "Fiat", " ", "*modelo*")]
    public void Criar_ComCampoObrigatorioVazio_DeveLancarExcecao(string placa, string marca, string modelo, string mensagem)
    {
        var act = () => VeiculoDaOrdem.Criar(placa, marca, modelo);

        act.Should().Throw<DomainException>().WithMessage(mensagem);
    }

    [Fact]
    public void Igualdade_ComMesmosValores_DeveSerIgual()
    {
        var veiculo = new VeiculoDaOrdemBuilder().ComPlaca("ABC1D23").ComMarca("Fiat").ComModelo("Uno").Build();
        var outro = new VeiculoDaOrdemBuilder().ComPlaca("ABC1D23").ComMarca("Fiat").ComModelo("Uno").Build();

        veiculo.Equals(outro).Should().BeTrue();
        (veiculo == outro).Should().BeTrue();
        (veiculo != outro).Should().BeFalse();
        veiculo.GetHashCode().Should().Be(outro.GetHashCode());
    }

    [Fact]
    public void Igualdade_ComValoresDiferentes_DeveSerDiferente()
    {
        var veiculo = new VeiculoDaOrdemBuilder().ComPlaca("ABC1D23").Build();
        var outro = new VeiculoDaOrdemBuilder().ComPlaca("XYZ9K88").Build();

        veiculo.Equals(outro).Should().BeFalse();
        (veiculo != outro).Should().BeTrue();
    }

    [Fact]
    public void Igualdade_ComNuloOuOutroTipo_DeveSerDiferente()
    {
        var veiculo = new VeiculoDaOrdemBuilder().Build();
        VeiculoDaOrdem? nulo = null;

        veiculo.Equals((object)"ABC1D23").Should().BeFalse();
        (veiculo == nulo).Should().BeFalse();
        (nulo == veiculo).Should().BeFalse();
        (nulo == null).Should().BeTrue();
        veiculo.Equals(null).Should().BeFalse();
    }
}
