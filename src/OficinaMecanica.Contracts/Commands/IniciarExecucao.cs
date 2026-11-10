using OficinaMecanica.Contracts.Common;

namespace OficinaMecanica.Contracts.Commands;

// Levo o snapshot aprovado (nao so o Numero) porque servicos e pecas podem mudar durante o
// diagnostico; assim a Execucao trabalha exatamente sobre o que o cliente aprovou e pagou.
public sealed record IniciarExecucao(
    Guid MessageId,
    Guid OrdemDeServicoId,
    DateTimeOffset OcorridoEm,
    string Numero,
    IReadOnlyList<ItemDeServicoMensagem> Servicos,
    IReadOnlyList<ItemDePecaMensagem> Pecas);
