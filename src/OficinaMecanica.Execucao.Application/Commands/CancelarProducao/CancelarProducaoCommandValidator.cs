using FluentValidation;

namespace OficinaMecanica.Execucao.Application.Commands.CancelarProducao;

public class CancelarProducaoCommandValidator : AbstractValidator<CancelarProducaoCommand>
{
    public CancelarProducaoCommandValidator()
    {
        RuleFor(x => x.OrdemDeServicoId).NotEmpty().WithMessage("A ordem de serviço é obrigatória.");

        RuleFor(x => x.Motivo)
            .NotEmpty().WithMessage("O motivo do cancelamento é obrigatório.")
            .MaximumLength(500).WithMessage("O motivo do cancelamento deve ter no máximo 500 caracteres.");
    }
}
