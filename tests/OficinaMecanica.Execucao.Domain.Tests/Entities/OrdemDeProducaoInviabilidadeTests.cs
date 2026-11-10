using FluentAssertions;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Domain.Exceptions;

namespace OficinaMecanica.Execucao.Domain.Tests.Entities;

public class OrdemDeProducaoInviabilidadeTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Inviabilizar_EmExecucao_DeveMarcarInviavelEInterromperOReparo()
    {
        var ordem = new OrdemDeProducaoBuilder().EmExecucao("reparador");

        ordem.Inviabilizar("reparador", "Peça indisponível no fornecedor", Agora);

        ordem.Status.Should().Be(StatusProducao.Inviavel);
        ordem.MotivoInviabilidade.Should().Be("Peça indisponível no fornecedor");
        ordem.InviabilizadaPor.Should().Be("reparador");
        var reparo = ordem.Etapas.Last();
        reparo.Nome.Should().Be(NomeEtapa.Reparo);
        reparo.Status.Should().Be(StatusEtapa.Interrompida);
        reparo.Fim.Should().Be(Agora);
    }

    [Fact]
    public void Inviabilizar_AguardandoExecucao_DeveMarcarInviavelSemEtapaDeReparo()
    {
        var ordem = new OrdemDeProducaoBuilder().AguardandoExecucao();

        ordem.Inviabilizar("supervisor-01", "Fornecedor descontinuou a peça", Agora);

        ordem.Status.Should().Be(StatusProducao.Inviavel);
        ordem.Etapas.Should().ContainSingle().Which.Nome.Should().Be(NomeEtapa.Diagnostico);
        ordem.Etapas.Single().Status.Should().Be(StatusEtapa.Concluida);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Inviabilizar_SemMotivo_DeveLancarExcecaoEManterOStatus(string motivo)
    {
        var ordem = new OrdemDeProducaoBuilder().EmExecucao();

        var act = () => ordem.Inviabilizar(ordem.MecanicoId!, motivo, Agora);

        act.Should().Throw<DomainException>().WithMessage("*motivo da inviabilidade*");
        ordem.Status.Should().Be(StatusProducao.EmExecucao);
        ordem.MotivoInviabilidade.Should().BeNull();
        ordem.InviabilizadaPor.Should().BeNull();
        ordem.Etapas.Last().Status.Should().Be(StatusEtapa.EmAndamento);
    }

    [Fact]
    public void Inviabilizar_EmExecucaoPorOutroMecanico_DeveLancarExcecaoEManterEmExecucao()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico("diagnosticador").EmExecucao("reparador");

        var act = () => ordem.Inviabilizar("diagnosticador", "Peça indisponível", Agora);

        act.Should().Throw<DomainException>().WithMessage("*mecânico responsável*");
        ordem.Status.Should().Be(StatusProducao.EmExecucao);
        ordem.MotivoInviabilidade.Should().BeNull();
        ordem.Etapas.Last().Status.Should().Be(StatusEtapa.EmAndamento);
    }

    [Fact]
    public void Inviabilizar_AguardandoExecucaoPorQualquerMecanicoIdentificado_DeveInviabilizar()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico("diagnosticador").AguardandoExecucao();

        ordem.Inviabilizar("outro-mecanico", "Peça indisponível", Agora);

        ordem.Status.Should().Be(StatusProducao.Inviavel);
        ordem.InviabilizadaPor.Should().Be("outro-mecanico");
        ordem.MecanicoId.Should().Be("diagnosticador");
    }

    [Theory]
    [InlineData(StatusProducao.AguardandoExecucao)]
    [InlineData(StatusProducao.EmExecucao)]
    public void Inviabilizar_SemMecanico_DeveLancarExcecaoEManterOStatus(StatusProducao status)
    {
        var ordem = new OrdemDeProducaoBuilder().NoStatus(status);

        var act = () => ordem.Inviabilizar("  ", "Peça indisponível", Agora);

        act.Should().Throw<DomainException>().WithMessage("*mecânico é obrigatório*");
        ordem.Status.Should().Be(status);
        ordem.MotivoInviabilidade.Should().BeNull();
    }

    [Fact]
    public void Inviabilizar_ComEspacosNoMotivo_DeveAparar()
    {
        var ordem = new OrdemDeProducaoBuilder().EmExecucao();

        ordem.Inviabilizar(ordem.MecanicoId!, "  Peça indisponível ", Agora);

        ordem.MotivoInviabilidade.Should().Be("Peça indisponível");
    }

    [Fact]
    public void Inviabilizar_DuasVezes_DeveLancarExcecaoNaSegunda()
    {
        var ordem = new OrdemDeProducaoBuilder().Inviavel();

        var act = () => ordem.Inviabilizar("mecanico-01", "outro motivo", Agora);

        act.Should().Throw<DomainException>().WithMessage("*inviabilizar a execução*");
    }
}
