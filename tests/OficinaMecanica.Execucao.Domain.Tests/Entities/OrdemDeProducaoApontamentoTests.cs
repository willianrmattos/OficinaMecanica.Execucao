using FluentAssertions;
using OficinaMecanica.Execucao.Domain.Exceptions;

namespace OficinaMecanica.Execucao.Domain.Tests.Entities;

public class OrdemDeProducaoApontamentoTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RegistrarApontamento_EmDiagnosticoPeloResponsavel_DeveAdicionar()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico("mecanico-01").EmDiagnostico();

        ordem.RegistrarApontamento("mecanico-01", "Ruído no cubo da roda", Agora);

        var apontamento = ordem.Apontamentos.Should().ContainSingle().Subject;
        apontamento.MecanicoId.Should().Be("mecanico-01");
        apontamento.Texto.Should().Be("Ruído no cubo da roda");
        apontamento.Em.Should().Be(Agora);
    }

    [Fact]
    public void RegistrarApontamento_EmExecucaoPeloResponsavel_DeveAdicionar()
    {
        var ordem = new OrdemDeProducaoBuilder().EmExecucao("mecanico-02");

        ordem.RegistrarApontamento("mecanico-02", "Cubo substituído", Agora);

        ordem.Apontamentos.Should().ContainSingle().Which.Texto.Should().Be("Cubo substituído");
    }

    [Fact]
    public void RegistrarApontamento_VariasVezes_DeveManterAOrdemDeRegistro()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico("mecanico-01").EmDiagnostico();

        ordem.RegistrarApontamento("mecanico-01", "primeiro", Agora);
        ordem.RegistrarApontamento("mecanico-01", "segundo", Agora.AddMinutes(1));

        ordem.Apontamentos.Select(a => a.Texto).Should().Equal("primeiro", "segundo");
    }

    [Fact]
    public void RegistrarApontamento_PorOutroMecanico_DeveLancarExcecaoSemRegistrar()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico("mecanico-01").EmDiagnostico();

        var act = () => ordem.RegistrarApontamento("mecanico-02", "intruso", Agora);

        act.Should().Throw<DomainException>().WithMessage("*mecânico responsável*");
        ordem.Apontamentos.Should().BeEmpty();
    }

    [Fact]
    public void RegistrarApontamento_AposTrocaDeResponsavelNoReparo_SoOMecanicoDoReparoPodeRegistrar()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico("diagnosticador").EmExecucao("reparador");

        var doDiagnostico = () => ordem.RegistrarApontamento("diagnosticador", "texto", Agora);
        var doReparo = () => ordem.RegistrarApontamento("reparador", "texto", Agora);

        doDiagnostico.Should().Throw<DomainException>().WithMessage("*mecânico responsável*");
        doReparo.Should().NotThrow();
        ordem.Apontamentos.Should().ContainSingle();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RegistrarApontamento_SemMecanico_DeveLancarExcecao(string mecanicoId)
    {
        var ordem = new OrdemDeProducaoBuilder().EmDiagnostico();

        var act = () => ordem.RegistrarApontamento(mecanicoId, "texto", Agora);

        act.Should().Throw<DomainException>().WithMessage("*mecânico é obrigatório*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RegistrarApontamento_SemTexto_DeveLancarExcecao(string texto)
    {
        var ordem = new OrdemDeProducaoBuilder().EmDiagnostico();

        var act = () => ordem.RegistrarApontamento(ordem.MecanicoId!, texto, Agora);

        act.Should().Throw<DomainException>().WithMessage("*texto do apontamento*");
        ordem.Apontamentos.Should().BeEmpty();
    }
}
