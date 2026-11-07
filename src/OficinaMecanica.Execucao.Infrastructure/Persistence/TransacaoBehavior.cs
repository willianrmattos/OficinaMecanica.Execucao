using MediatR;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using OficinaMecanica.Execucao.Domain.Exceptions;
using OficinaMecanica.Execucao.Infrastructure.Observability;

namespace OficinaMecanica.Execucao.Infrastructure.Persistence;

public sealed record TransacaoOptions
{
    public int MaximoDeTentativas { get; init; } = 8;

    // Base do backoff com jitter: o retry imediato recairia no mesmo conflito de escrita.
    public int AtrasoBaseEmMs { get; init; } = 20;
}

// Caminho HTTP: abre a transacao Mongo ao redor do handler, para o documento e o evento do outbox
// commitarem juntos, e reexecuta o handler INTEIRO quando o servidor rotula a falha como
// TransientTransactionError (WriteConflict, perda de primario). O driver nao refaz o corpo da transacao
// sozinho, e a releitura do handler e o que enxerga o estado ja commitado pelo concorrente.
// Dentro de um consumer ja existe uma transacao (a do filtro do outbox) e quem retenta e o
// UseMessageRetry, que fica antes do outbox: aqui so repasso.
public sealed class TransacaoBehavior<TRequest, TResponse>(
    ITransacaoMongo transacao,
    IMetricasPendentes metricas,
    TransacaoOptions opcoes,
    ILogger<TransacaoBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!PoliticaDeTransacao.Exige(typeof(TRequest)) || transacao.Ambiente)
            return await next();

        for (var tentativa = 1; ; tentativa++)
        {
            await transacao.IniciarAsync(cancellationToken);

            try
            {
                return await next();
            }
            catch (MongoException ex) when (ex.HasErrorLabel("TransientTransactionError"))
            {
                // Encerro antes do backoff para nao segurar a transacao aberta durante a espera.
                await EncerrarAsync();

                if (tentativa >= opcoes.MaximoDeTentativas)
                {
                    logger.LogWarning(ex, "Transação de {Requisicao} abortada por conflito em {Tentativas} tentativas.",
                        typeof(TRequest).Name, tentativa);
                    throw new ConcorrenciaException("A operação não pôde ser concluída por conflito de concorrência; tente novamente.");
                }

                logger.LogInformation("Transação de {Requisicao} abortada por conflito (tentativa {Tentativa}); reexecutando.",
                    typeof(TRequest).Name, tentativa);
                await Task.Delay(Atraso(tentativa), cancellationToken);
            }
            finally
            {
                // Um handler que termina sem SaveChanges (fila vazia, reentrega ignorada) nao tem o que
                // confirmar: a transacao ainda aberta e abortada e as metricas nao publicadas, descartadas.
                await EncerrarAsync();
            }
        }
    }

    private TimeSpan Atraso(int tentativa) =>
        TimeSpan.FromMilliseconds(Random.Shared.Next(opcoes.AtrasoBaseEmMs, opcoes.AtrasoBaseEmMs * 2) * tentativa);

    private async Task EncerrarAsync()
    {
        await transacao.AbortarAsync();
        metricas.Descartar();
    }
}
