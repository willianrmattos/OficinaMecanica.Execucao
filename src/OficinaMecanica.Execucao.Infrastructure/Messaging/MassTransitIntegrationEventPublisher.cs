using System.Linq.Expressions;
using System.Reflection;
using MassTransit;
using OficinaMecanica.Execucao.Application.Interfaces;

namespace OficinaMecanica.Execucao.Infrastructure.Messaging;

// So publica: o IPublishEndpoint do escopo decide o destino. Dentro de um consumer com outbox grava no
// outbox na transacao do consumer; no escopo HTTP (UseBusOutbox) grava na transacao aberta pelo
// TransacaoBehavior. Em ambos o commit leva a mensagem junto com o estado da ordem.
public sealed class MassTransitIntegrationEventPublisher(IPublishEndpoint publishEndpoint) : IIntegrationEventPublisher
{
    public Task PublishAsync<T>(T mensagem, CancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(mensagem);

        // O MessageId do contrato e gerado pelo produtor e identifica o fato de negocio; copiado para o
        // envelope e ele que o inbox do consumidor usa para deduplicar a entrega.
        var messageId = AcessorDeMessageId<T>.Obter(mensagem);

        return publishEndpoint.Publish(
            mensagem,
            contexto =>
            {
                if (messageId.HasValue)
                    contexto.MessageId = messageId.Value;
            },
            cancellationToken);
    }

    // Um delegate compilado por tipo de mensagem, criado uma unica vez: evita reflection a cada publicacao.
    private static class AcessorDeMessageId<T>
    {
        private static readonly Func<T, Guid>? Leitor = Criar();

        public static Guid? Obter(T mensagem) => Leitor?.Invoke(mensagem);

        private static Func<T, Guid>? Criar()
        {
            var propriedade = typeof(T).GetProperty("MessageId", BindingFlags.Instance | BindingFlags.Public);

            if (propriedade is null || propriedade.PropertyType != typeof(Guid) || !propriedade.CanRead)
                return null;

            var parametro = Expression.Parameter(typeof(T), "mensagem");
            return Expression.Lambda<Func<T, Guid>>(Expression.Property(parametro, propriedade), parametro).Compile();
        }
    }
}
