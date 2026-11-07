using FluentAssertions;
using OficinaMecanica.Execucao.Application.Commands.CancelarProducao;
using OficinaMecanica.Execucao.Application.Commands.EnfileirarOrdemDeProducao;
using OficinaMecanica.Execucao.Application.Commands.LiberarParaExecucao;

namespace OficinaMecanica.Execucao.Application.Tests.Commands;

public class MapeamentoDeMensagensTests
{
    [Fact]
    public void SolicitarDiagnostico_DeveVirarEnfileirarOrdemDeProducaoCommand()
    {
        var mensagem = new SolicitarDiagnosticoBuilder().Build();

        var command = EnfileirarOrdemDeProducaoCommand.DaMensagem(mensagem);

        command.OrdemDeServicoId.Should().Be(mensagem.OrdemDeServicoId);
        command.Numero.Should().Be(mensagem.Numero);
        command.Placa.Should().Be(mensagem.Placa);
        command.Marca.Should().Be(mensagem.Marca);
        command.Modelo.Should().Be(mensagem.Modelo);
        command.Observacoes.Should().Be(mensagem.Observacoes);
        command.Servicos.Should().BeEquivalentTo(mensagem.Servicos);
        command.Pecas.Should().BeEquivalentTo(mensagem.Pecas);
    }

    [Fact]
    public void SolicitarDiagnosticoSemObservacoes_DeveVirarCommandComObservacoesNulas()
    {
        var mensagem = new SolicitarDiagnosticoBuilder().SemObservacoes().Build();

        var command = EnfileirarOrdemDeProducaoCommand.DaMensagem(mensagem);

        command.Observacoes.Should().BeNull();
    }

    [Fact]
    public void IniciarExecucao_DeveVirarLiberarParaExecucaoCommandComOSnapshotAprovado()
    {
        var mensagem = new IniciarExecucaoBuilder().Build();

        var command = LiberarParaExecucaoCommand.DaMensagem(mensagem);

        command.OrdemDeServicoId.Should().Be(mensagem.OrdemDeServicoId);
        command.Servicos.Should().BeEquivalentTo(mensagem.Servicos);
        command.Pecas.Should().BeEquivalentTo(mensagem.Pecas);
    }

    [Fact]
    public void CancelarProducao_DeveVirarCancelarProducaoCommand()
    {
        var mensagem = new CancelarProducaoBuilder().Build();

        var command = CancelarProducaoCommand.DaMensagem(mensagem);

        command.OrdemDeServicoId.Should().Be(mensagem.OrdemDeServicoId);
        command.Motivo.Should().Be(mensagem.Motivo);
    }
}
