using MassTransit;
using MediatR;
using OficinaMecanica.Contracts.Commands;
using OficinaMecanica.Execucao.Application.Commands.LiberarParaExecucao;

namespace OficinaMecanica.Execucao.Infrastructure.Messaging.Consumers;

public sealed class IniciarExecucaoConsumer(IMediator mediator) : IConsumer<IniciarExecucao>
{
    public Task Consume(ConsumeContext<IniciarExecucao> context) =>
        mediator.Send(LiberarParaExecucaoCommand.DaMensagem(context.Message), context.CancellationToken);
}
