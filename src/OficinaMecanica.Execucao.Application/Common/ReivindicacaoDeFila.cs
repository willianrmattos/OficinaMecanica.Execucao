using Microsoft.Extensions.Logging;
using OficinaMecanica.Execucao.Domain.Entities;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Domain.Exceptions;
using OficinaMecanica.Execucao.Domain.Interfaces;

namespace OficinaMecanica.Execucao.Application.Common;

internal static class ReivindicacaoDeFila
{
    private const int MaximoDeTentativas = 5;

    // Optei por reivindicar lendo a proxima ordem e gravando com checagem de Version: se outro
    // mecanico levou a mesma ordem, a gravacao falha com ConcorrenciaException e a leitura seguinte
    // ja devolve a proxima da fila. Dois mecanicos nunca ficam com a mesma ordem, sem lock distribuido.
    // Devolve a ordem ja atualizada (ainda sem commit) ou null quando a fila esta vazia.
    public static async Task<OrdemDeProducao?> ReivindicarAsync(
        IOrdemDeProducaoRepository repositorio,
        StatusProducao fila,
        Action<OrdemDeProducao> reivindicar,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        for (var tentativa = 1; ; tentativa++)
        {
            var ordem = await repositorio.ObterProximaDaFilaAsync(fila, cancellationToken);
            if (ordem is null)
                return null;

            reivindicar(ordem);

            try
            {
                await repositorio.AtualizarAsync(ordem, cancellationToken);
                return ordem;
            }
            catch (ConcorrenciaException) when (tentativa < MaximoDeTentativas)
            {
                logger.LogInformation(
                    "Ordem {OrdemDeServicoId} da fila {Fila} foi reivindicada por outro mecânico (tentativa {Tentativa}); buscando a próxima.",
                    ordem.Id, fila, tentativa);
            }
        }
    }
}
