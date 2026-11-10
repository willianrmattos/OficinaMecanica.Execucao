using FluentAssertions;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Domain.Exceptions;

namespace OficinaMecanica.Execucao.Domain.Tests.Entities;

public class OrdemDeProducaoCancelamentoTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(StatusProducao.Fila)]
    [InlineData(StatusProducao.EmDiagnostico)]
    [InlineData(StatusProducao.DiagnosticoConcluido)]
    [InlineData(StatusProducao.AguardandoExecucao)]
    public void Cancelar_EmStatusPermitido_DeveCancelarERegistrarOMotivo(StatusProducao statusInicial)
    {
        var ordem = new OrdemDeProducaoBuilder().NoStatus(statusInicial);

        var cancelada = ordem.Cancelar("Pagamento cancelado", Agora);

        cancelada.Should().BeTrue();
        ordem.Status.Should().Be(StatusProducao.Cancelada);
        ordem.MotivoCancelamento.Should().Be("Pagamento cancelado");
    }

    [Fact]
    public void Cancelar_ComEspacosNoMotivo_DeveAparar()
    {
        var ordem = new OrdemDeProducaoBuilder().NaFila();

        ordem.Cancelar("  Pagamento cancelado ", Agora);

        ordem.MotivoCancelamento.Should().Be("Pagamento cancelado");
    }

    [Fact]
    public void Cancelar_EmDiagnostico_DeveInterromperAEtapaDeDiagnostico()
    {
        var ordem = new OrdemDeProducaoBuilder().EmDiagnostico();

        ordem.Cancelar("Cliente desistiu", Agora);

        var etapa = ordem.Etapas.Should().ContainSingle().Subject;
        etapa.Status.Should().Be(StatusEtapa.Interrompida);
        etapa.Fim.Should().Be(Agora);
    }

    [Fact]
    public void Cancelar_AposDiagnosticoConcluido_DeveManterAEtapaConcluida()
    {
        var ordem = new OrdemDeProducaoBuilder().DiagnosticoConcluido();

        ordem.Cancelar("Cliente desistiu", Agora);

        ordem.Etapas.Should().ContainSingle().Which.Status.Should().Be(StatusEtapa.Concluida);
    }

    [Fact]
    public void Cancelar_Reentrega_DeveSerIdempotenteEManterOMotivoOriginal()
    {
        var ordem = new OrdemDeProducaoBuilder().Cancelada("Pagamento cancelado");

        var segunda = ordem.Cancelar("Outro motivo", Agora.AddHours(1));

        segunda.Should().BeFalse();
        ordem.Status.Should().Be(StatusProducao.Cancelada);
        ordem.MotivoCancelamento.Should().Be("Pagamento cancelado");
    }

    [Fact]
    public void Cancelar_ReentregaSemMotivo_DeveSerIdempotenteSemLancarExcecao()
    {
        var ordem = new OrdemDeProducaoBuilder().Cancelada();

        var act = () => ordem.Cancelar("", Agora);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(StatusProducao.EmExecucao)]
    [InlineData(StatusProducao.Finalizada)]
    [InlineData(StatusProducao.Inviavel)]
    public void Cancelar_EmStatusNaoPermitido_DeveLancarExcecaoEManterOStatus(StatusProducao statusInicial)
    {
        var ordem = new OrdemDeProducaoBuilder().NoStatus(statusInicial);

        var act = () => ordem.Cancelar("Pagamento cancelado", Agora);

        act.Should().Throw<DomainException>().WithMessage("*cancelar a produção*");
        ordem.Status.Should().Be(statusInicial);
        ordem.MotivoCancelamento.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Cancelar_SemMotivo_DeveLancarExcecaoEManterOStatus(string motivo)
    {
        var ordem = new OrdemDeProducaoBuilder().NaFila();

        var act = () => ordem.Cancelar(motivo, Agora);

        act.Should().Throw<DomainException>().WithMessage("*motivo do cancelamento*");
        ordem.Status.Should().Be(StatusProducao.Fila);
    }
}
