using FluentValidation;
using OficinaMecanica.Execucao.Application.Common;

namespace OficinaMecanica.Execucao.Application.Commands.LiberarParaExecucao;

public class LiberarParaExecucaoCommandValidator : AbstractValidator<LiberarParaExecucaoCommand>
{
    public LiberarParaExecucaoCommandValidator()
    {
        RuleFor(x => x.OrdemDeServicoId).NotEmpty().WithMessage("A ordem de serviço é obrigatória.");

        RuleFor(x => x.Servicos).NotEmpty().WithMessage("O snapshot aprovado deve ter pelo menos um serviço.");
        RuleForEach(x => x.Servicos).SetValidator(new ItemDeServicoMensagemValidator());

        RuleFor(x => x.Pecas).NotNull().WithMessage("A lista de peças é obrigatória (pode ser vazia).");
        RuleForEach(x => x.Pecas).SetValidator(new ItemDePecaMensagemValidator());
    }
}
