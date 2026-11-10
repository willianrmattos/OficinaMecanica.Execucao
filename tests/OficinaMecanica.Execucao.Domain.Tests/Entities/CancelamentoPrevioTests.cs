using FluentAssertions;
using OficinaMecanica.Execucao.Domain.Exceptions;

namespace OficinaMecanica.Execucao.Domain.Tests.Entities;

public class CancelamentoPrevioTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Construtor_ComDadosValidos_DeveRegistrarOCancelamento()
    {
        var ordemId = Guid.NewGuid();

        var cancelamento = new CancelamentoPrevioBuilder()
            .DaOrdemDeServico(ordemId)
            .ComMotivo("Pagamento cancelado")
            .RegistradoEm(Agora)
            .Build();

        cancelamento.Id.Should().Be(ordemId);
        cancelamento.OrdemDeServicoId.Should().Be(ordemId);
        cancelamento.Motivo.Should().Be("Pagamento cancelado");
        cancelamento.RegistradoEm.Should().Be(Agora);
    }

    [Fact]
    public void Construtor_ComEspacosNoMotivo_DeveAparar()
    {
        var cancelamento = new CancelamentoPrevioBuilder().ComMotivo("  Pagamento cancelado ").Build();

        cancelamento.Motivo.Should().Be("Pagamento cancelado");
    }

    [Fact]
    public void Construtor_SemOrdemDeServico_DeveLancarExcecao()
    {
        var act = () => new CancelamentoPrevioBuilder().DaOrdemDeServico(Guid.Empty).Build();

        act.Should().Throw<DomainException>().WithMessage("*ordem de serviço*obrigatória*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Construtor_SemMotivo_DeveLancarExcecao(string motivo)
    {
        var act = () => new CancelamentoPrevioBuilder().ComMotivo(motivo).Build();

        act.Should().Throw<DomainException>().WithMessage("*motivo*obrigatório*");
    }
}
