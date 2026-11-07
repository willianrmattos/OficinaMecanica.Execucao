namespace OficinaMecanica.Execucao.Application.Interfaces;

// Optei por publicar sempre ANTES de IUnitOfWork.SaveChangesAsync: a implementacao grava a mensagem
// no outbox dentro da mesma transacao do commit, entao publicar depois perderia a atomicidade
// entre o estado da ordem e a mensagem enviada a saga.
public interface IIntegrationEventPublisher
{
    Task PublishAsync<T>(T mensagem, CancellationToken cancellationToken = default) where T : class;
}
