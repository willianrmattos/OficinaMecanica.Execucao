using MediatR;
using MensagemCancelarProducao = OficinaMecanica.Contracts.Commands.CancelarProducao;

namespace OficinaMecanica.Execucao.Application.Commands.CancelarProducao;

public record CancelarProducaoCommand(Guid OrdemDeServicoId, string Motivo) : IRequest
{
    // Uso alias porque, dentro deste namespace, o nome simples CancelarProducao resolve para o proprio
    // namespace da pasta e nao para o contrato de mensagem.
    public static CancelarProducaoCommand DaMensagem(MensagemCancelarProducao mensagem) =>
        new(mensagem.OrdemDeServicoId, mensagem.Motivo);
}
