using MassTransit;
using MediatR;
using OficinaMecanica.Contracts.Commands;
using OficinaMecanica.Execucao.Application.Commands.EnfileirarOrdemDeProducao;

namespace OficinaMecanica.Execucao.Infrastructure.Messaging.Consumers;

// Consumer fino: so traduz o contrato para o command. Regra de negocio e idempotencia ficam no handler.
public sealed class SolicitarDiagnosticoConsumer(IMediator mediator) : IConsumer<SolicitarDiagnostico>
{
    public Task Consume(ConsumeContext<SolicitarDiagnostico> context) =>
        mediator.Send(EnfileirarOrdemDeProducaoCommand.DaMensagem(context.Message), context.CancellationToken);
}
