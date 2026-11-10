using FluentAssertions;
using OficinaMecanica.Execucao.Application.DTOs;

namespace OficinaMecanica.Execucao.Application.Tests.DTOs;

public class OrdemDeProducaoMapperTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ToDto_OrdemFinalizada_DeveMapearTodosOsCampos()
    {
        var servico = new ServicoDaOrdemBuilder().ComNome("Troca de óleo").ComQuantidade(1).ComPrecoUnitario(120.50m).Build();
        var peca = new PecaDaOrdemBuilder().ComNome("Filtro de óleo").ComQuantidade(2).ComPrecoUnitario(35.90m).Build();
        var laudo = new LaudoBuilder().ComTexto("Troca recomendada").ComHorasEstimadas(1.5m).Build();
        var ordem = new OrdemDeProducaoBuilder()
            .ComServicos(servico)
            .ComPecas(peca)
            .ComObservacoes("Barulho no motor")
            .DoMecanico("mecanico-01")
            .EnfileiradaEm(Agora)
            .DiagnosticoConcluido(laudo);
        ordem.LiberarParaExecucao(new[] { servico }, new[] { peca }, Agora.AddMinutes(10));
        ordem.IniciarReparo("mecanico-02", Agora.AddMinutes(11));
        ordem.RegistrarApontamento("mecanico-02", "Óleo drenado", Agora.AddMinutes(12));
        ordem.FinalizarReparo("mecanico-02", "Concluído", Agora.AddMinutes(13));

        var dto = OrdemDeProducaoMapper.ToDto(ordem);

        dto.Id.Should().Be(ordem.Id);
        dto.Numero.Should().Be(ordem.Numero);
        dto.Veiculo.Should().Be(new VeiculoDaOrdemDto(ordem.Veiculo.Placa, ordem.Veiculo.Marca, ordem.Veiculo.Modelo));
        dto.Observacoes.Should().Be("Barulho no motor");
        dto.Status.Should().Be("Finalizada");
        dto.MecanicoId.Should().Be("mecanico-02");
        dto.Servicos.Should().Equal(new ServicoDaOrdemDto(servico.ServicoId, "Troca de óleo", 1, 120.50m));
        dto.Pecas.Should().Equal(new PecaDaOrdemDto(peca.PecaId, "Filtro de óleo", 2, 35.90m));
        dto.Etapas.Should().HaveCount(2);
        dto.Etapas.Last().Should().Be(new EtapaProducaoDto("Reparo", "mecanico-02", "Concluida", Agora.AddMinutes(11), Agora.AddMinutes(13)));
        dto.Apontamentos.Should().Equal(new ApontamentoDto("mecanico-02", "Óleo drenado", Agora.AddMinutes(12)));
        dto.Laudo.Should().Be(new LaudoDto("Troca recomendada", 1.5m));
        dto.Resumo.Should().Be("Concluído");
        dto.MotivoInviabilidade.Should().BeNull();
        dto.InviabilizadaPor.Should().BeNull();
        dto.MotivoCancelamento.Should().BeNull();
        dto.EnfileiradaEm.Should().Be(Agora);
        dto.LiberadaEm.Should().Be(Agora.AddMinutes(10));
    }

    [Fact]
    public void ToDto_OrdemRecemEnfileirada_DeveTerColecoesVaziasEOpcionaisNulos()
    {
        var ordem = new OrdemDeProducaoBuilder().SemObservacoes().NaFila();

        var dto = OrdemDeProducaoMapper.ToDto(ordem);

        dto.Observacoes.Should().BeNull();
        dto.Status.Should().Be("Fila");
        dto.MecanicoId.Should().BeNull();
        dto.Etapas.Should().BeEmpty();
        dto.Apontamentos.Should().BeEmpty();
        dto.Laudo.Should().BeNull();
        dto.Resumo.Should().BeNull();
        dto.LiberadaEm.Should().BeNull();
    }

    [Fact]
    public void ToDto_OrdemInviavel_DeveExporOMotivoEAEtapaInterrompida()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico("mecanico-01").Inviavel("Peça indisponível");

        var dto = OrdemDeProducaoMapper.ToDto(ordem);

        dto.Status.Should().Be("Inviavel");
        dto.MotivoInviabilidade.Should().Be("Peça indisponível");
        dto.InviabilizadaPor.Should().Be("mecanico-01");
        dto.Etapas.Last().Status.Should().Be("Interrompida");
    }

    [Fact]
    public void ToDto_OrdemCancelada_DeveExporOMotivoDoCancelamento()
    {
        var ordem = new OrdemDeProducaoBuilder().Cancelada("Pagamento cancelado");

        var dto = OrdemDeProducaoMapper.ToDto(ordem);

        dto.Status.Should().Be("Cancelada");
        dto.MotivoCancelamento.Should().Be("Pagamento cancelado");
    }

    [Fact]
    public void ToResumoDto_DeveMapearOsCamposDaListagem()
    {
        var ordem = new OrdemDeProducaoBuilder().DoMecanico("mecanico-01").EmDiagnostico();

        var dto = OrdemDeProducaoMapper.ToResumoDto(ordem);

        dto.Should().Be(new OrdemDeProducaoResumoDto(
            ordem.Id, ordem.Numero, ordem.Veiculo.Placa, ordem.Veiculo.Marca, ordem.Veiculo.Modelo,
            "EmDiagnostico", "mecanico-01", ordem.EnfileiradaEm));
    }

    [Fact]
    public void ResultadoDaReivindicacao_DeveDiferenciarFilaVaziaDeOrdemReivindicada()
    {
        var ordemDto = OrdemDeProducaoMapper.ToDto(new OrdemDeProducaoBuilder().EmDiagnostico());

        ResultadoDaReivindicacaoDto.Vazia().Should().Be(new ResultadoDaReivindicacaoDto(true, null));
        ResultadoDaReivindicacaoDto.Reivindicada(ordemDto).Should().Be(new ResultadoDaReivindicacaoDto(false, ordemDto));
    }
}
