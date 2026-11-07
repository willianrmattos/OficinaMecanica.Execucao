using FluentAssertions;
using OficinaMecanica.Execucao.Domain.Exceptions;
using OficinaMecanica.Execucao.Domain.ValueObjects;

namespace OficinaMecanica.Execucao.Domain.Tests.ValueObjects;

public class ApontamentoTests
{
    private static readonly DateTimeOffset Em = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Criar_ComDadosValidos_DeveGuardarOsValores()
    {
        var apontamento = Apontamento.Criar("mecanico-01", "Pastilhas trocadas", Em);

        apontamento.MecanicoId.Should().Be("mecanico-01");
        apontamento.Texto.Should().Be("Pastilhas trocadas");
        apontamento.Em.Should().Be(Em);
    }

    [Fact]
    public void Criar_ComEspacosNoTexto_DeveAparar()
    {
        var apontamento = Apontamento.Criar("mecanico-01", "  Pastilhas trocadas ", Em);

        apontamento.Texto.Should().Be("Pastilhas trocadas");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Criar_SemMecanico_DeveLancarExcecao(string mecanicoId)
    {
        var act = () => Apontamento.Criar(mecanicoId, "Pastilhas trocadas", Em);

        act.Should().Throw<DomainException>().WithMessage("*mecânico*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Criar_SemTexto_DeveLancarExcecao(string texto)
    {
        var act = () => Apontamento.Criar("mecanico-01", texto, Em);

        act.Should().Throw<DomainException>().WithMessage("*texto do apontamento*");
    }

    [Fact]
    public void Igualdade_ComMesmosValores_DeveSerIgual()
    {
        var apontamento = Apontamento.Criar("mecanico-01", "Pastilhas trocadas", Em);
        var outro = Apontamento.Criar("mecanico-01", "Pastilhas trocadas", Em);

        apontamento.Should().Be(outro);
        apontamento.GetHashCode().Should().Be(outro.GetHashCode());
    }
}
