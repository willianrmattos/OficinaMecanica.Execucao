using OficinaMecanica.Execucao.Application.Commands.ConcluirDiagnostico;
using OficinaMecanica.Execucao.Application.Commands.FinalizarReparo;
using OficinaMecanica.Execucao.Application.Commands.InviabilizarExecucao;
using OficinaMecanica.Execucao.Application.Commands.ReivindicarProximoDiagnostico;

namespace OficinaMecanica.Execucao.Infrastructure.Persistence;

// So abro transacao (e pago o retry de TransientTransactionError) onde ela e indispensavel: nos commands
// cujo handler publica no outbox, porque a mudanca de estado e a mensagem precisam commitar juntas. Os
// demais gravam um unico documento com checagem de Version, que ja e atomico. Um teste confere que esta
// lista coincide com os handlers que dependem de IIntegrationEventPublisher.
public static class PoliticaDeTransacao
{
    private static readonly HashSet<Type> Transacionais =
    [
        typeof(ReivindicarProximoDiagnosticoCommand),
        typeof(ConcluirDiagnosticoCommand),
        typeof(FinalizarReparoCommand),
        typeof(InviabilizarExecucaoCommand)
    ];

    public static IReadOnlyCollection<Type> RequisicoesTransacionais => Transacionais;

    public static bool Exige(Type tipoDaRequisicao) => Transacionais.Contains(tipoDaRequisicao);
}
