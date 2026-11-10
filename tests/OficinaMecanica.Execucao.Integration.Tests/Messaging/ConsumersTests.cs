using MassTransit;
using MediatR;
using Moq;
using OficinaMecanica.Contracts.Commands;
using OficinaMecanica.Execucao.Application.Commands.CancelarProducao;
using OficinaMecanica.Execucao.Application.Commands.EnfileirarOrdemDeProducao;
using OficinaMecanica.Execucao.Application.Commands.LiberarParaExecucao;
using OficinaMecanica.Execucao.Infrastructure.Messaging.Consumers;

namespace OficinaMecanica.Execucao.Integration.Tests.Messaging;

// Os consumers so traduzem o contrato para o command MediatR; a regra e a idempotencia estao nos handlers.
public class ConsumersTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly CancellationTokenSource _cancelamento = new();

    private Mock<ConsumeContext<T>> Contexto<T>(T mensagem) where T : class
    {
        var contexto = new Mock<ConsumeContext<T>>();
        contexto.SetupGet(c => c.Message).Returns(mensagem);
        contexto.SetupGet(c => c.CancellationToken).Returns(_cancelamento.Token);
        return contexto;
    }

    [Fact]
    public async Task SolicitarDiagnosticoConsumer_DeveEnviarOCommandDeEnfileirarComOsDadosDaMensagem()
    {
        var mensagem = new SolicitarDiagnosticoBuilder().Build();
        EnfileirarOrdemDeProducaoCommand? enviado = null;
        _mediator
            .Setup(m => m.Send(It.IsAny<EnfileirarOrdemDeProducaoCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest, CancellationToken>((comando, _) => enviado = (EnfileirarOrdemDeProducaoCommand)comando)
            .Returns(Task.CompletedTask);

        await new SolicitarDiagnosticoConsumer(_mediator.Object).Consume(Contexto(mensagem).Object);

        enviado.Should().BeEquivalentTo(EnfileirarOrdemDeProducaoCommand.DaMensagem(mensagem));
        enviado!.OrdemDeServicoId.Should().Be(mensagem.OrdemDeServicoId);
        _mediator.Verify(m => m.Send(It.IsAny<EnfileirarOrdemDeProducaoCommand>(), _cancelamento.Token), Times.Once);
    }

    [Fact]
    public async Task IniciarExecucaoConsumer_DeveEnviarOCommandDeLiberarComOSnapshotAprovado()
    {
        var mensagem = new IniciarExecucaoBuilder().Build();
        LiberarParaExecucaoCommand? enviado = null;
        _mediator
            .Setup(m => m.Send(It.IsAny<LiberarParaExecucaoCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest, CancellationToken>((comando, _) => enviado = (LiberarParaExecucaoCommand)comando)
            .Returns(Task.CompletedTask);

        await new IniciarExecucaoConsumer(_mediator.Object).Consume(Contexto(mensagem).Object);

        enviado.Should().BeEquivalentTo(LiberarParaExecucaoCommand.DaMensagem(mensagem));
        enviado!.Servicos.Should().BeEquivalentTo(mensagem.Servicos);
        enviado.Pecas.Should().BeEquivalentTo(mensagem.Pecas);
    }

    [Fact]
    public async Task CancelarProducaoConsumer_DeveEnviarOCommandDeCancelarComOMotivo()
    {
        var mensagem = new CancelarProducaoBuilder().ComMotivo("Pagamento expirado.").Build();
        CancelarProducaoCommand? enviado = null;
        _mediator
            .Setup(m => m.Send(It.IsAny<CancelarProducaoCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest, CancellationToken>((comando, _) => enviado = (CancelarProducaoCommand)comando)
            .Returns(Task.CompletedTask);

        await new CancelarProducaoConsumer(_mediator.Object).Consume(Contexto(mensagem).Object);

        enviado.Should().Be(new CancelarProducaoCommand(mensagem.OrdemDeServicoId, "Pagamento expirado."));
    }

    [Fact]
    public async Task Consumer_QuandoOHandlerFalha_DevePropagarAExcecaoParaOMassTransitDecidirORetry()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<CancelarProducaoCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("falha transitoria"));

        var acao = () => new CancelarProducaoConsumer(_mediator.Object).Consume(Contexto(new CancelarProducaoBuilder().Build()).Object);

        await acao.Should().ThrowAsync<InvalidOperationException>().WithMessage("falha transitoria");
    }
}
