using MediatR;
using OficinaMecanica.Contracts.Commands;
using OficinaMecanica.Contracts.Common;

namespace OficinaMecanica.Execucao.Application.Commands.EnfileirarOrdemDeProducao;

public record EnfileirarOrdemDeProducaoCommand(
    Guid OrdemDeServicoId,
    string Numero,
    string Placa,
    string Marca,
    string Modelo,
    string? Observacoes,
    IReadOnlyList<ItemDeServicoMensagem> Servicos,
    IReadOnlyList<ItemDePecaMensagem> Pecas
) : IRequest
{
    public static EnfileirarOrdemDeProducaoCommand DaMensagem(SolicitarDiagnostico mensagem) =>
        new(
            mensagem.OrdemDeServicoId,
            mensagem.Numero,
            mensagem.Placa,
            mensagem.Marca,
            mensagem.Modelo,
            mensagem.Observacoes,
            mensagem.Servicos,
            mensagem.Pecas);
}
