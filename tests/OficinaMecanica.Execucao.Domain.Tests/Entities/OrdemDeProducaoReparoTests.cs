using FluentAssertions;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Domain.Exceptions;

namespace OficinaMecanica.Execucao.Domain.Tests.Entities;

public class OrdemDeProducaoReparoTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IniciarReparo_AguardandoExecucao_DeveIrParaEmExecucaoComOMecanicoDoReparo()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico("diagnosticador").AguardandoExecucao();

        ordem.IniciarReparo("reparador", Agora);

        ordem.Status.Should().Be(StatusProducao.EmExecucao);
        ordem.MecanicoId.Should().Be("reparador");
        ordem.Etapas.Should().HaveCount(2);
        var reparo = ordem.Etapas.Last();
        reparo.Nome.Should().Be(NomeEtapa.Reparo);
        reparo.MecanicoId.Should().Be("reparador");
        reparo.Status.Should().Be(StatusEtapa.EmAndamento);
        reparo.Inicio.Should().Be(Agora);
        reparo.Fim.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void IniciarReparo_SemMecanico_DeveLancarExcecaoEManterAguardando(string mecanicoId)
    {
        var ordem = new OrdemDeProducaoBuilder().AguardandoExecucao();

        var act = () => ordem.IniciarReparo(mecanicoId, Agora);

        act.Should().Throw<DomainException>().WithMessage("*mecânico é obrigatório*");
        ordem.Status.Should().Be(StatusProducao.AguardandoExecucao);
    }

    [Fact]
    public void FinalizarReparo_PeloMecanicoResponsavel_DeveFinalizarEFecharEtapa()
    {
        var ordem = new OrdemDeProducaoBuilder().EmExecucao("reparador");

        ordem.FinalizarReparo("reparador", "Serviço concluído sem intercorrências", Agora);

        ordem.Status.Should().Be(StatusProducao.Finalizada);
        ordem.Resumo.Should().Be("Serviço concluído sem intercorrências");
        var reparo = ordem.Etapas.Last();
        reparo.Status.Should().Be(StatusEtapa.Concluida);
        reparo.Fim.Should().Be(Agora);
    }

    [Fact]
    public void FinalizarReparo_ComEspacosNoResumo_DeveAparar()
    {
        var ordem = new OrdemDeProducaoBuilder().EmExecucao("reparador");

        ordem.FinalizarReparo("reparador", "  Serviço concluído ", Agora);

        ordem.Resumo.Should().Be("Serviço concluído");
    }

    [Fact]
    public void FinalizarReparo_PorOutroMecanico_DeveLancarExcecaoEManterEmExecucao()
    {
        var ordem = new OrdemDeProducaoBuilder().EmExecucao("reparador");

        var act = () => ordem.FinalizarReparo("outro", "resumo", Agora);

        act.Should().Throw<DomainException>().WithMessage("*mecânico responsável*");
        ordem.Status.Should().Be(StatusProducao.EmExecucao);
        ordem.Resumo.Should().BeNull();
    }

    [Fact]
    public void FinalizarReparo_PeloMecanicoDoDiagnosticoQuandoOReparoFoiDeOutro_DeveLancarExcecao()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico("diagnosticador").EmExecucao("reparador");

        var act = () => ordem.FinalizarReparo("diagnosticador", "resumo", Agora);

        act.Should().Throw<DomainException>().WithMessage("*mecânico responsável*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FinalizarReparo_SemResumo_DeveLancarExcecao(string resumo)
    {
        var ordem = new OrdemDeProducaoBuilder().EmExecucao();

        var act = () => ordem.FinalizarReparo(ordem.MecanicoId!, resumo, Agora);

        act.Should().Throw<DomainException>().WithMessage("*resumo da execução*");
        ordem.Status.Should().Be(StatusProducao.EmExecucao);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FinalizarReparo_SemMecanico_DeveLancarExcecao(string mecanicoId)
    {
        var ordem = new OrdemDeProducaoBuilder().EmExecucao();

        var act = () => ordem.FinalizarReparo(mecanicoId, "resumo", Agora);

        act.Should().Throw<DomainException>().WithMessage("*mecânico é obrigatório*");
    }

    [Fact]
    public void FluxoCompleto_DeveTerDuasEtapasConcluidasEmOrdem()
    {
        var ordem = new OrdemDeProducaoBuilder().Finalizada();

        ordem.Status.Should().Be(StatusProducao.Finalizada);
        ordem.Etapas.Select(e => e.Nome).Should().Equal(NomeEtapa.Diagnostico, NomeEtapa.Reparo);
        ordem.Etapas.Should().OnlyContain(e => e.Status == StatusEtapa.Concluida && e.Fim.HasValue);
        ordem.Laudo.Should().NotBeNull();
        ordem.Resumo.Should().NotBeNullOrWhiteSpace();
    }
}
