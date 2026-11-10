using FluentAssertions;
using OficinaMecanica.Execucao.Domain.Exceptions;
using OficinaMecanica.Execucao.Domain.ValueObjects;

namespace OficinaMecanica.Execucao.Domain.Tests.ValueObjects;

public class LaudoTests
{
    [Fact]
    public void Criar_ComTextoEHoras_DeveGuardarOsValores()
    {
        var laudo = Laudo.Criar("Troca de óleo e filtro recomendada", 1.5m);

        laudo.Texto.Should().Be("Troca de óleo e filtro recomendada");
        laudo.HorasEstimadas.Should().Be(1.5m);
    }

    [Fact]
    public void Criar_SemHorasEstimadas_DeveAceitar()
    {
        var laudo = new LaudoBuilder().ComHorasEstimadas(null).Build();

        laudo.HorasEstimadas.Should().BeNull();
    }

    [Fact]
    public void Criar_ComEspacosNasPontas_DeveAparar()
    {
        var laudo = new LaudoBuilder().ComTexto("  Freio desgastado  ").Build();

        laudo.Texto.Should().Be("Freio desgastado");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Criar_SemTexto_DeveLancarExcecao(string texto)
    {
        var act = () => new LaudoBuilder().ComTexto(texto).Build();

        act.Should().Throw<DomainException>().WithMessage("*texto do laudo*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void Criar_ComHorasNaoPositivas_DeveLancarExcecao(int horas)
    {
        var act = () => new LaudoBuilder().ComHorasEstimadas((decimal)horas).Build();

        act.Should().Throw<DomainException>().WithMessage("*horas estimadas*maiores que zero*");
    }

    [Fact]
    public void Igualdade_ComHorasNulasDeAmbosOsLados_DeveSerIgual()
    {
        var laudo = new LaudoBuilder().ComTexto("Revisão geral").ComHorasEstimadas(null).Build();
        var outro = new LaudoBuilder().ComTexto("Revisão geral").ComHorasEstimadas(null).Build();

        laudo.Should().Be(outro);
        laudo.GetHashCode().Should().Be(outro.GetHashCode());
    }

    [Fact]
    public void Igualdade_ComHorasDiferentes_DeveSerDiferente()
    {
        var laudo = new LaudoBuilder().ComTexto("Revisão geral").ComHorasEstimadas(2m).Build();
        var outro = new LaudoBuilder().ComTexto("Revisão geral").ComHorasEstimadas(null).Build();

        laudo.Should().NotBe(outro);
    }
}
