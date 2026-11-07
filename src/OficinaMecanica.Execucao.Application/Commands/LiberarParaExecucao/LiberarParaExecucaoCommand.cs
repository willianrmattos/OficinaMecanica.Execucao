using MediatR;
using OficinaMecanica.Contracts.Commands;
using OficinaMecanica.Contracts.Common;

namespace OficinaMecanica.Execucao.Application.Commands.LiberarParaExecucao;

public record LiberarParaExecucaoCommand(
    Guid OrdemDeServicoId,
    IReadOnlyList<ItemDeServicoMensagem> Servicos,
    IReadOnlyList<ItemDePecaMensagem> Pecas
) : IRequest
{
    public static LiberarParaExecucaoCommand DaMensagem(IniciarExecucao mensagem) =>
        new(mensagem.OrdemDeServicoId, mensagem.Servicos, mensagem.Pecas);
}
