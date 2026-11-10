using OficinaMecanica.Contracts.Commands;
using OficinaMecanica.Contracts.Common;
using OficinaMecanica.Contracts.Events;

namespace OficinaMecanica.Contracts.Tests;

// Valores fixos de proposito: o golden JSON so serve como trava de compatibilidade se a amostra
// que o gera for deterministica (nada de Guid.NewGuid() nem DateTime.Now aqui).
internal static class AmostrasDeContratos
{
    private static readonly Guid MensagemId = Id(1);
    private static readonly Guid OrdemId = Id(2);
    private static readonly DateTimeOffset Ocorrido = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<ItemDeServicoMensagem> Servicos =
        new[] { new ItemDeServicoMensagem(Id(10), "Troca de óleo", 120.50m, 1) };

    private static readonly IReadOnlyList<ItemDePecaMensagem> Pecas =
        new[] { new ItemDePecaMensagem(Id(11), "Filtro de óleo", 35.90m, 2) };

    public static IReadOnlyDictionary<string, object> Todas { get; } = new Dictionary<string, object>
    {
        [nameof(SolicitarDiagnostico)] = new SolicitarDiagnostico(MensagemId, OrdemId, Ocorrido,
            "OS-20261107-ABC123", "ABC1D23", "Fiat", "Uno", "Barulho no motor", Servicos, Pecas),
        [nameof(IniciarExecucao)] = new IniciarExecucao(MensagemId, OrdemId, Ocorrido, "OS-20261107-ABC123", Servicos, Pecas),
        [nameof(CancelarProducao)] = new CancelarProducao(MensagemId, OrdemId, Ocorrido, "Pagamento cancelado"),

        [nameof(DiagnosticoIniciado)] = new DiagnosticoIniciado(MensagemId, OrdemId, Ocorrido, "mecanico-01"),
        [nameof(DiagnosticoConcluido)] = new DiagnosticoConcluido(MensagemId, OrdemId, Ocorrido, "mecanico-01", "Troca de óleo e filtro recomendada"),
        [nameof(ExecucaoFinalizada)] = new ExecucaoFinalizada(MensagemId, OrdemId, Ocorrido, "Serviço concluído sem intercorrências", Ocorrido),
        [nameof(ExecucaoInviavel)] = new ExecucaoInviavel(MensagemId, OrdemId, Ocorrido, "Peça indisponível no fornecedor"),
    };

    // Variantes com os campos opcionais nulos e listas vazias: sao elas que pegam um consumidor
    // antigo quebrando com um campo ausente, o que o caminho feliz nunca mostra.
    public static IReadOnlyDictionary<string, object> ComNulos { get; } = new Dictionary<string, object>
    {
        [nameof(SolicitarDiagnostico)] = new SolicitarDiagnostico(MensagemId, OrdemId, Ocorrido,
            "OS-20261107-ABC123", "ABC1D23", "Fiat", "Uno", null,
            Array.Empty<ItemDeServicoMensagem>(), Array.Empty<ItemDePecaMensagem>()),
    };

    private static Guid Id(int n) => new($"00000000-0000-0000-0000-{n:D12}");
}
