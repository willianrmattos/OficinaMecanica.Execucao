using FluentAssertions;
using OficinaMecanica.Execucao.Domain.Entities;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Domain.Exceptions;
using OficinaMecanica.Execucao.Domain.ValueObjects;

namespace OficinaMecanica.Execucao.Domain.Tests.Entities;

public class OrdemDeProducaoTransicoesInvalidasTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    public static IEnumerable<object[]> InvalidosParaIniciarDiagnostico() =>
        Exceto(StatusProducao.Fila);

    public static IEnumerable<object[]> InvalidosParaRegistrarApontamento() =>
        Exceto(StatusProducao.EmDiagnostico, StatusProducao.EmExecucao);

    public static IEnumerable<object[]> InvalidosParaConcluirDiagnostico() =>
        Exceto(StatusProducao.EmDiagnostico);

    public static IEnumerable<object[]> InvalidosParaLiberarParaExecucao() =>
        Exceto(
            StatusProducao.DiagnosticoConcluido,
            StatusProducao.AguardandoExecucao,
            StatusProducao.EmExecucao,
            StatusProducao.Finalizada,
            StatusProducao.Inviavel);

    public static IEnumerable<object[]> InvalidosParaIniciarReparo() =>
        Exceto(StatusProducao.AguardandoExecucao);

    public static IEnumerable<object[]> InvalidosParaFinalizarReparo() =>
        Exceto(StatusProducao.EmExecucao);

    public static IEnumerable<object[]> InvalidosParaInviabilizar() =>
        Exceto(StatusProducao.AguardandoExecucao, StatusProducao.EmExecucao);

    [Theory]
    [MemberData(nameof(InvalidosParaIniciarDiagnostico))]
    public void IniciarDiagnostico_ForaDaFila_DeveLancarExcecao(StatusProducao status)
    {
        var ordem = new OrdemDeProducaoBuilder().NoStatus(status);

        var act = () => ordem.IniciarDiagnostico("mecanico-01", Agora);

        act.Should().Throw<DomainException>().WithMessage("*iniciar o diagnóstico*");
        ordem.Status.Should().Be(status);
    }

    [Theory]
    [MemberData(nameof(InvalidosParaRegistrarApontamento))]
    public void RegistrarApontamento_ForaDeDiagnosticoOuExecucao_DeveLancarExcecao(StatusProducao status)
    {
        var ordem = new OrdemDeProducaoBuilder().NoStatus(status);

        var act = () => ordem.RegistrarApontamento(ordem.MecanicoId ?? "mecanico-01", "texto", Agora);

        act.Should().Throw<DomainException>().WithMessage("*registrar apontamento*");
        ordem.Apontamentos.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(InvalidosParaConcluirDiagnostico))]
    public void ConcluirDiagnostico_ForaDeEmDiagnostico_DeveLancarExcecao(StatusProducao status)
    {
        var ordem = new OrdemDeProducaoBuilder().NoStatus(status);

        var act = () => ordem.ConcluirDiagnostico(ordem.MecanicoId ?? "mecanico-01", new LaudoBuilder().Build(), Agora);

        act.Should().Throw<DomainException>().WithMessage("*concluir o diagnóstico*");
        ordem.Status.Should().Be(status);
    }

    [Theory]
    [MemberData(nameof(InvalidosParaLiberarParaExecucao))]
    public void LiberarParaExecucao_AntesDoDiagnosticoConcluidoOuCancelada_DeveLancarExcecao(StatusProducao status)
    {
        var ordem = new OrdemDeProducaoBuilder().NoStatus(status);

        var act = () => ordem.LiberarParaExecucao(
            new[] { new ServicoDaOrdemBuilder().Build() }, Array.Empty<PecaDaOrdem>(), Agora);

        act.Should().Throw<DomainException>().WithMessage("*liberar para execução*");
        ordem.Status.Should().Be(status);
    }

    [Theory]
    [MemberData(nameof(InvalidosParaIniciarReparo))]
    public void IniciarReparo_ForaDeAguardandoExecucao_DeveLancarExcecao(StatusProducao status)
    {
        var ordem = new OrdemDeProducaoBuilder().NoStatus(status);

        var act = () => ordem.IniciarReparo("mecanico-01", Agora);

        act.Should().Throw<DomainException>().WithMessage("*iniciar o reparo*");
        ordem.Status.Should().Be(status);
    }

    [Theory]
    [MemberData(nameof(InvalidosParaFinalizarReparo))]
    public void FinalizarReparo_ForaDeEmExecucao_DeveLancarExcecao(StatusProducao status)
    {
        var ordem = new OrdemDeProducaoBuilder().NoStatus(status);

        var act = () => ordem.FinalizarReparo(ordem.MecanicoId ?? "mecanico-01", "resumo", Agora);

        act.Should().Throw<DomainException>().WithMessage("*finalizar o reparo*");
        ordem.Status.Should().Be(status);
    }

    [Theory]
    [MemberData(nameof(InvalidosParaInviabilizar))]
    public void Inviabilizar_ForaDeAguardandoOuEmExecucao_DeveLancarExcecao(StatusProducao status)
    {
        var ordem = new OrdemDeProducaoBuilder().NoStatus(status);

        var act = () => ordem.Inviabilizar(ordem.MecanicoId ?? "mecanico-01", "motivo", Agora);

        act.Should().Throw<DomainException>().WithMessage("*inviabilizar a execução*");
        ordem.Status.Should().Be(status);
    }

    [Fact]
    public void Mensagem_DeveInformarOStatusAtualEOsPermitidos()
    {
        var ordem = new OrdemDeProducaoBuilder().NaFila();

        var act = () => ordem.IniciarReparo("mecanico-01", Agora);

        act.Should().Throw<DomainException>()
            .WithMessage("*'Fila'*permitido*'AguardandoExecucao'*");
    }

    [Fact]
    public void NoStatus_ComValorForaDoEnum_DeveLancarArgumentOutOfRange()
    {
        var act = () => new OrdemDeProducaoBuilder().NoStatus((StatusProducao)999);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static IEnumerable<object[]> Exceto(params StatusProducao[] permitidos) =>
        Enum.GetValues<StatusProducao>().Except(permitidos).Select(status => new object[] { status });
}
