using MassTransit;
using MediatR;
using OficinaMecanica.Execucao.Application.Commands.CancelarProducao;
using MensagemCancelarProducao = OficinaMecanica.Contracts.Commands.CancelarProducao;

namespace OficinaMecanica.Execucao.Infrastructure.Messaging.Consumers;

public sealed class CancelarProducaoConsumer(IMediator mediator) : IConsumer<MensagemCancelarProducao>
{
    public Task Consume(ConsumeContext<MensagemCancelarProducao> context) =>
        mediator.Send(CancelarProducaoCommand.DaMensagem(context.Message), context.CancellationToken);
}
