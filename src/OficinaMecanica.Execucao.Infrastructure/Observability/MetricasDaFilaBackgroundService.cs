using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OficinaMecanica.Execucao.Application.Queries.ObterResumoDaFila;
using OficinaMecanica.Execucao.Domain.Enums;

namespace OficinaMecanica.Execucao.Infrastructure.Observability;

public sealed record MetricasDaFilaOptions
{
    public int IntervaloDeAtualizacaoEmSegundos { get; init; } = 15;
}

// Atualiza o retrato lido pelos gauges da fila. Uma falha de leitura (banco indisponivel) so registra aviso
// e mantem o ultimo retrato: metrica nao pode derrubar o servico.
public sealed class MetricasDaFilaBackgroundService(
    IServiceScopeFactory escopos,
    MetricasDaFila metricas,
    MetricasDaFilaOptions opcoes,
    ILogger<MetricasDaFilaBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var temporizador = new PeriodicTimer(TimeSpan.FromSeconds(opcoes.IntervaloDeAtualizacaoEmSegundos));

        try
        {
            do
            {
                await AtualizarAsync(stoppingToken);
            }
            while (await temporizador.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Encerramento do host.
        }
    }

    public async Task AtualizarAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var escopo = escopos.CreateScope();

            var resumo = await escopo.ServiceProvider.GetRequiredService<IMediator>()
                .Send(new ObterResumoDaFilaQuery(), cancellationToken);
            var inicioDaMaisAntiga = await escopo.ServiceProvider.GetRequiredService<IIdadeDaFilaReader>()
                .ObterInicioDaMaisAntigaAsync(cancellationToken);

            var outboxPendente = await escopo.ServiceProvider.GetRequiredService<IOutboxPendenteReader>()
                .ContarAsync(cancellationToken);

            var tamanhos = resumo.PorStatus.ToDictionary(x => Enum.Parse<StatusProducao>(x.Key), x => x.Value);
            metricas.Atualizar(tamanhos, inicioDaMaisAntiga, outboxPendente);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Não foi possível atualizar as métricas da fila; mantendo o último retrato.");
        }
    }
}
