using OficinaMecanica.Execucao.Domain.Interfaces;
using OficinaMecanica.Execucao.Infrastructure.Observability;

namespace OficinaMecanica.Execucao.Infrastructure.Persistence;

// No Mongo as escritas dos repositorios ja acontecem na hora; o que o SaveChanges faz aqui e confirmar a
// transacao que carrega a ordem E a mensagem do outbox. Dentro de um consumer a transacao e a do filtro
// do outbox do MassTransit, que a confirma quando o consumer termina: confirmar duas vezes nao agrega nada.
public sealed class MongoUnitOfWork(ITransacaoMongo transacao, IMetricasPendentes metricas) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await transacao.ConfirmarAsync(cancellationToken);
        metricas.Publicar();
    }
}
