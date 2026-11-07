using FluentAssertions;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Domain.Exceptions;
using OficinaMecanica.Execucao.Domain.ValueObjects;

namespace OficinaMecanica.Execucao.Domain.Tests.Entities;

public class OrdemDeProducaoDiagnosticoTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IniciarDiagnostico_NaFila_DeveIrParaEmDiagnosticoComEtapaAberta()
    {
        var ordem = new OrdemDeProducaoBuilder().NaFila();

        ordem.IniciarDiagnostico("mecanico-01", Agora);

        ordem.Status.Should().Be(StatusProducao.EmDiagnostico);
        ordem.MecanicoId.Should().Be("mecanico-01");
        var etapa = ordem.Etapas.Should().ContainSingle().Subject;
        etapa.Nome.Should().Be(NomeEtapa.Diagnostico);
        etapa.MecanicoId.Should().Be("mecanico-01");
        etapa.Status.Should().Be(StatusEtapa.EmAndamento);
        etapa.Inicio.Should().Be(Agora);
        etapa.Fim.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void IniciarDiagnostico_SemMecanico_DeveLancarExcecaoEManterNaFila(string mecanicoId)
    {
        var ordem = new OrdemDeProducaoBuilder().NaFila();

        var act = () => ordem.IniciarDiagnostico(mecanicoId, Agora);

        act.Should().Throw<DomainException>().WithMessage("*mecânico é obrigatório*");
        ordem.Status.Should().Be(StatusProducao.Fila);
        ordem.Etapas.Should().BeEmpty();
    }

    [Fact]
    public void ConcluirDiagnostico_PeloMecanicoResponsavel_DeveGuardarLaudoEFecharEtapa()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico("mecanico-01").EmDiagnostico();
        var laudo = new LaudoBuilder().ComTexto("Troca de óleo recomendada").ComHorasEstimadas(1.5m).Build();

        ordem.ConcluirDiagnostico("mecanico-01", laudo, Agora);

        ordem.Status.Should().Be(StatusProducao.DiagnosticoConcluido);
        ordem.Laudo.Should().Be(laudo);
        var etapa = ordem.Etapas.Should().ContainSingle().Subject;
        etapa.Status.Should().Be(StatusEtapa.Concluida);
        etapa.Fim.Should().Be(Agora);
    }

    [Fact]
    public void ConcluirDiagnostico_PorOutroMecanico_DeveLancarExcecaoEManterEmDiagnostico()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico("mecanico-01").EmDiagnostico();

        var act = () => ordem.ConcluirDiagnostico("mecanico-02", new LaudoBuilder().Build(), Agora);

        act.Should().Throw<DomainException>().WithMessage("*mecânico responsável*");
        ordem.Status.Should().Be(StatusProducao.EmDiagnostico);
        ordem.Laudo.Should().BeNull();
        ordem.Etapas.Single().Status.Should().Be(StatusEtapa.EmAndamento);
    }

    [Fact]
    public void ConcluirDiagnostico_ComCaixaDiferenteNoIdDoMecanico_DeveLancarExcecao()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico("Mecanico-01").EmDiagnostico();

        var act = () => ordem.ConcluirDiagnostico("mecanico-01", new LaudoBuilder().Build(), Agora);

        act.Should().Throw<DomainException>().WithMessage("*mecânico responsável*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ConcluirDiagnostico_SemMecanico_DeveLancarExcecao(string mecanicoId)
    {
        var ordem = new OrdemDeProducaoBuilder().EmDiagnostico();

        var act = () => ordem.ConcluirDiagnostico(mecanicoId, new LaudoBuilder().Build(), Agora);

        act.Should().Throw<DomainException>().WithMessage("*mecânico é obrigatório*");
    }

    [Fact]
    public void ConcluirDiagnostico_ComLaudoNulo_DeveLancarArgumentNull()
    {
        var ordem = new OrdemDeProducaoBuilder().EmDiagnostico();

        var act = () => ordem.ConcluirDiagnostico(ordem.MecanicoId!, null!, Agora);

        act.Should().Throw<ArgumentNullException>().WithParameterName("laudo");
    }

    [Fact]
    public void ConcluirDiagnostico_DuasVezes_DeveLancarExcecaoNaSegunda()
    {
        var ordem = new OrdemDeProducaoBuilder().DiagnosticoConcluido();

        var act = () => ordem.ConcluirDiagnostico(ordem.MecanicoId!, new LaudoBuilder().Build(), Agora);

        act.Should().Throw<DomainException>().WithMessage("*concluir o diagnóstico*");
    }
}
