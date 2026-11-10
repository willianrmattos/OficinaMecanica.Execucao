using FluentAssertions;
using OficinaMecanica.Execucao.Domain.Entities;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Domain.Exceptions;
using OficinaMecanica.Execucao.Domain.ValueObjects;

namespace OficinaMecanica.Execucao.Domain.Tests.Entities;

public class OrdemDeProducaoLiberacaoTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LiberarParaExecucao_DeDiagnosticoConcluido_DeveIrParaAguardandoExecucao()
    {
        var ordem = new OrdemDeProducaoBuilder().DiagnosticoConcluido();

        var liberada = ordem.LiberarParaExecucao(
            new[] { new ServicoDaOrdemBuilder().Build() }, new[] { new PecaDaOrdemBuilder().Build() }, Agora);

        liberada.Should().BeTrue();
        ordem.Status.Should().Be(StatusProducao.AguardandoExecucao);
        ordem.LiberadaEm.Should().Be(Agora);
    }

    [Theory]
    [InlineData(StatusProducao.AguardandoExecucao)]
    [InlineData(StatusProducao.EmExecucao)]
    [InlineData(StatusProducao.Finalizada)]
    [InlineData(StatusProducao.Inviavel)]
    public void LiberadaEm_DeQualquerOrdemQuePassouPelaFilaDeReparo_DeveEstarPreenchida(StatusProducao status)
    {
        var ordem = new OrdemDeProducaoBuilder().EnfileiradaEm(Agora).NoStatus(status);

        ordem.LiberadaEm.Should().Be(Agora.AddMinutes(3), "a liberação não pode ser sobrescrita por IniciarReparo, Finalizar ou Inviabilizar");
    }

    [Theory]
    [InlineData(StatusProducao.Fila)]
    [InlineData(StatusProducao.EmDiagnostico)]
    [InlineData(StatusProducao.DiagnosticoConcluido)]
    public void LiberadaEm_DeOrdemQueAindaNaoFoiLiberada_DeveSerNula(StatusProducao status)
    {
        var ordem = new OrdemDeProducaoBuilder().NoStatus(status);

        ordem.LiberadaEm.Should().BeNull();
    }

    [Fact]
    public void LiberarParaExecucao_DeveSubstituirServicosEPecasPeloSnapshotAprovado()
    {
        var enfileirado = new ServicoDaOrdemBuilder().ComNome("Troca de óleo").Build();
        var aprovadoServico = new ServicoDaOrdemBuilder().ComNome("Troca de óleo e filtro").Build();
        var aprovadaPeca = new PecaDaOrdemBuilder().ComNome("Filtro de óleo").Build();
        var ordem = new OrdemDeProducaoBuilder().ComServicos(enfileirado).ComPecas().DiagnosticoConcluido();

        ordem.LiberarParaExecucao(new[] { aprovadoServico }, new[] { aprovadaPeca }, Agora);

        ordem.Servicos.Should().Equal(aprovadoServico);
        ordem.Pecas.Should().Equal(aprovadaPeca);
    }

    [Fact]
    public void LiberarParaExecucao_ComSnapshotSemPecas_DeveRemoverAsPecasEnfileiradas()
    {
        var ordem = new OrdemDeProducaoBuilder().DiagnosticoConcluido();

        ordem.LiberarParaExecucao(new[] { new ServicoDaOrdemBuilder().Build() }, Array.Empty<PecaDaOrdem>(), Agora);

        ordem.Pecas.Should().BeEmpty();
        ordem.Status.Should().Be(StatusProducao.AguardandoExecucao);
    }

    [Fact]
    public void LiberarParaExecucao_DeveCopiarOSnapshotRecebido()
    {
        var servicos = new List<ServicoDaOrdem> { new ServicoDaOrdemBuilder().Build() };
        var pecas = new List<PecaDaOrdem> { new PecaDaOrdemBuilder().Build() };
        var ordem = new OrdemDeProducaoBuilder().DiagnosticoConcluido();

        ordem.LiberarParaExecucao(servicos, pecas, Agora);
        servicos.Clear();
        pecas.Clear();

        ordem.Servicos.Should().HaveCount(1);
        ordem.Pecas.Should().HaveCount(1);
    }

    [Fact]
    public void LiberarParaExecucao_Reentrega_DeveSerIdempotenteESemSobrescreverOSnapshot()
    {
        var ordem = new OrdemDeProducaoBuilder().DiagnosticoConcluido();
        var servicoAprovado = new ServicoDaOrdemBuilder().ComNome("Aprovado").Build();
        ordem.LiberarParaExecucao(new[] { servicoAprovado }, Array.Empty<PecaDaOrdem>(), Agora);

        var segunda = ordem.LiberarParaExecucao(
            new[] { new ServicoDaOrdemBuilder().ComNome("Reentrega tardia").Build() },
            new[] { new PecaDaOrdemBuilder().Build() },
            Agora.AddMinutes(10));

        segunda.Should().BeFalse();
        ordem.Status.Should().Be(StatusProducao.AguardandoExecucao);
        ordem.Servicos.Should().Equal(servicoAprovado);
        ordem.Pecas.Should().BeEmpty();
        ordem.LiberadaEm.Should().Be(Agora);
    }

    [Theory]
    [InlineData(StatusProducao.EmExecucao)]
    [InlineData(StatusProducao.Finalizada)]
    [InlineData(StatusProducao.Inviavel)]
    public void LiberarParaExecucao_ReentregaTardiaDepoisDeAvancar_DeveSerNoOpSemAlterarAOrdem(StatusProducao status)
    {
        var ordem = new OrdemDeProducaoBuilder().NoStatus(status);
        var servicosAtuais = ordem.Servicos.ToList();
        var pecasAtuais = ordem.Pecas.ToList();
        var liberadaEm = ordem.LiberadaEm;

        var liberada = ordem.LiberarParaExecucao(
            new[] { new ServicoDaOrdemBuilder().ComNome("Reentrega tardia").Build() },
            Array.Empty<PecaDaOrdem>(),
            Agora.AddHours(1));

        liberada.Should().BeFalse();
        ordem.Status.Should().Be(status);
        ordem.Servicos.Should().Equal(servicosAtuais);
        ordem.Pecas.Should().Equal(pecasAtuais);
        ordem.LiberadaEm.Should().Be(liberadaEm);
    }

    [Fact]
    public void LiberarParaExecucao_DeOrdemCancelada_DeveLancarExcecao()
    {
        var ordem = new OrdemDeProducaoBuilder().Cancelada();

        var act = () => ordem.LiberarParaExecucao(
            new[] { new ServicoDaOrdemBuilder().Build() }, Array.Empty<PecaDaOrdem>(), Agora);

        act.Should().Throw<DomainException>().WithMessage("*liberar para execução*'Cancelada'*");
        ordem.Status.Should().Be(StatusProducao.Cancelada);
    }

    [Fact]
    public void LiberarParaExecucao_SemServicos_DeveLancarExcecaoEManterOEstado()
    {
        var ordem = new OrdemDeProducaoBuilder().DiagnosticoConcluido();
        var servicosOriginais = ordem.Servicos.ToList();

        var act = () => ordem.LiberarParaExecucao(Array.Empty<ServicoDaOrdem>(), Array.Empty<PecaDaOrdem>(), Agora);

        act.Should().Throw<DomainException>().WithMessage("*pelo menos um serviço*");
        ordem.Status.Should().Be(StatusProducao.DiagnosticoConcluido);
        ordem.Servicos.Should().Equal(servicosOriginais);
        ordem.LiberadaEm.Should().BeNull();
    }

    [Fact]
    public void LiberarParaExecucao_ComServicosNulos_DeveLancarArgumentNull()
    {
        var ordem = new OrdemDeProducaoBuilder().DiagnosticoConcluido();

        var act = () => ordem.LiberarParaExecucao(null!, Array.Empty<PecaDaOrdem>(), Agora);

        act.Should().Throw<ArgumentNullException>().WithParameterName("servicosAprovados");
    }

    [Fact]
    public void LiberarParaExecucao_ComPecasNulas_DeveLancarArgumentNull()
    {
        var ordem = new OrdemDeProducaoBuilder().DiagnosticoConcluido();

        var act = () => ordem.LiberarParaExecucao(new[] { new ServicoDaOrdemBuilder().Build() }, null!, Agora);

        act.Should().Throw<ArgumentNullException>().WithParameterName("pecasAprovadas");
    }
}
