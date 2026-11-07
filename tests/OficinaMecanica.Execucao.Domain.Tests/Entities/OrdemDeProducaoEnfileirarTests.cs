using FluentAssertions;
using OficinaMecanica.Execucao.Domain.Entities;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Domain.Exceptions;
using OficinaMecanica.Execucao.Domain.ValueObjects;

namespace OficinaMecanica.Execucao.Domain.Tests.Entities;

public class OrdemDeProducaoEnfileirarTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Enfileirar_ComDadosValidos_DeveCriarNaFila()
    {
        var ordemDeServicoId = Guid.NewGuid();
        var veiculo = new VeiculoDaOrdemBuilder().Build();
        var servico = new ServicoDaOrdemBuilder().Build();
        var peca = new PecaDaOrdemBuilder().Build();

        var ordem = new OrdemDeProducaoBuilder()
            .ComId(ordemDeServicoId)
            .ComNumero("OS-20261107-ABC123")
            .ComVeiculo(veiculo)
            .ComObservacoes("Barulho no motor")
            .ComServicos(servico)
            .ComPecas(peca)
            .EnfileiradaEm(Agora)
            .NaFila();

        ordem.Id.Should().Be(ordemDeServicoId);
        ordem.Numero.Should().Be("OS-20261107-ABC123");
        ordem.Veiculo.Should().Be(veiculo);
        ordem.Observacoes.Should().Be("Barulho no motor");
        ordem.Servicos.Should().Equal(servico);
        ordem.Pecas.Should().Equal(peca);
        ordem.Status.Should().Be(StatusProducao.Fila);
        ordem.EnfileiradaEm.Should().Be(Agora);
        ordem.MecanicoId.Should().BeNull();
        ordem.Laudo.Should().BeNull();
        ordem.Resumo.Should().BeNull();
        ordem.MotivoInviabilidade.Should().BeNull();
        ordem.InviabilizadaPor.Should().BeNull();
        ordem.MotivoCancelamento.Should().BeNull();
        ordem.LiberadaEm.Should().BeNull();
        ordem.Etapas.Should().BeEmpty();
        ordem.Apontamentos.Should().BeEmpty();
        ordem.Version.Should().Be(0);
    }

    [Fact]
    public void Enfileirar_SemObservacoes_DeveGuardarNulo()
    {
        var ordem = new OrdemDeProducaoBuilder().SemObservacoes().NaFila();

        ordem.Observacoes.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Enfileirar_ComObservacoesEmBranco_DeveGuardarNulo(string observacoes)
    {
        var ordem = new OrdemDeProducaoBuilder().ComObservacoes(observacoes).NaFila();

        ordem.Observacoes.Should().BeNull();
    }

    [Fact]
    public void Enfileirar_ComEspacosNasObservacoes_DeveAparar()
    {
        var ordem = new OrdemDeProducaoBuilder().ComObservacoes("  Barulho no motor ").NaFila();

        ordem.Observacoes.Should().Be("Barulho no motor");
    }

    [Fact]
    public void Enfileirar_SemPecas_DeveAceitar()
    {
        var ordem = new OrdemDeProducaoBuilder().ComPecas().NaFila();

        ordem.Pecas.Should().BeEmpty();
        ordem.Servicos.Should().HaveCount(1);
    }

    [Fact]
    public void Enfileirar_ComEspacosNoNumero_DeveAparar()
    {
        var ordem = new OrdemDeProducaoBuilder().ComNumero("  OS-1  ").NaFila();

        ordem.Numero.Should().Be("OS-1");
    }

    [Fact]
    public void Enfileirar_DeveCopiarAsColecoesRecebidas()
    {
        var servicos = new List<ServicoDaOrdem> { new ServicoDaOrdemBuilder().Build() };
        var pecas = new List<PecaDaOrdem> { new PecaDaOrdemBuilder().Build() };

        var ordem = OrdemDeProducao.Enfileirar(
            Guid.NewGuid(), "OS-1", new VeiculoDaOrdemBuilder().Build(), servicos, pecas, null, Agora);
        servicos.Clear();
        pecas.Clear();

        ordem.Servicos.Should().HaveCount(1);
        ordem.Pecas.Should().HaveCount(1);
    }

    [Fact]
    public void Enfileirar_ComIdVazio_DeveLancarExcecao()
    {
        var act = () => new OrdemDeProducaoBuilder().ComId(Guid.Empty).NaFila();

        act.Should().Throw<DomainException>().WithMessage("*identificador da ordem de serviço*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Enfileirar_SemNumero_DeveLancarExcecao(string numero)
    {
        var act = () => new OrdemDeProducaoBuilder().ComNumero(numero).NaFila();

        act.Should().Throw<DomainException>().WithMessage("*número da ordem*");
    }

    [Fact]
    public void Enfileirar_SemServicos_DeveAceitarPoisOSnapshotAprovadoJaExigeServico()
    {
        var ordem = new OrdemDeProducaoBuilder().ComServicos().NaFila();

        ordem.Servicos.Should().BeEmpty();
        ordem.Status.Should().Be(StatusProducao.Fila);
    }

    [Fact]
    public void Enfileirar_ComVeiculoNulo_DeveLancarArgumentNull()
    {
        var act = () => OrdemDeProducao.Enfileirar(
            Guid.NewGuid(), "OS-1", null!, new[] { new ServicoDaOrdemBuilder().Build() }, Array.Empty<PecaDaOrdem>(), null, Agora);

        act.Should().Throw<ArgumentNullException>().WithParameterName("veiculo");
    }

    [Fact]
    public void Enfileirar_ComServicosNulos_DeveLancarArgumentNull()
    {
        var act = () => OrdemDeProducao.Enfileirar(
            Guid.NewGuid(), "OS-1", new VeiculoDaOrdemBuilder().Build(), null!, Array.Empty<PecaDaOrdem>(), null, Agora);

        act.Should().Throw<ArgumentNullException>().WithParameterName("servicos");
    }

    [Fact]
    public void Enfileirar_ComPecasNulas_DeveLancarArgumentNull()
    {
        var act = () => OrdemDeProducao.Enfileirar(
            Guid.NewGuid(), "OS-1", new VeiculoDaOrdemBuilder().Build(), new[] { new ServicoDaOrdemBuilder().Build() }, null!, null, Agora);

        act.Should().Throw<ArgumentNullException>().WithParameterName("pecas");
    }

    [Fact]
    public void Identidade_DuasOrdensComOMesmoId_DevemSerIguais()
    {
        var id = Guid.NewGuid();
        var ordem = new OrdemDeProducaoBuilder().ComId(id).NaFila();
        var outra = new OrdemDeProducaoBuilder().ComId(id).EmDiagnostico();

        ordem.Equals(outra).Should().BeTrue();
        ordem.GetHashCode().Should().Be(outra.GetHashCode());
    }

    [Fact]
    public void Identidade_ComIdsDiferentesOuOutroTipo_DevemSerDiferentes()
    {
        var ordem = new OrdemDeProducaoBuilder().NaFila();
        var outra = new OrdemDeProducaoBuilder().NaFila();

        ordem.Equals(outra).Should().BeFalse();
        ordem.Equals(ordem).Should().BeTrue();
        ordem.Equals((object)ordem.Id).Should().BeFalse();
        ordem.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void IncrementVersion_DeveSomarUm()
    {
        var ordem = new OrdemDeProducaoBuilder().NaFila();

        ordem.IncrementVersion();
        ordem.IncrementVersion();

        ordem.Version.Should().Be(2);
    }
}
