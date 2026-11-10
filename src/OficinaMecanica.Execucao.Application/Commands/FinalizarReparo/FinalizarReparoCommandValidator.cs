using FluentValidation;

namespace OficinaMecanica.Execucao.Application.Commands.FinalizarReparo;

public class FinalizarReparoCommandValidator : AbstractValidator<FinalizarReparoCommand>
{
    public FinalizarReparoCommandValidator()
    {
        RuleFor(x => x.OrdemDeServicoId).NotEmpty().WithMessage("A ordem de serviço é obrigatória.");

        RuleFor(x => x.MecanicoId)
            .NotEmpty().WithMessage("O mecânico é obrigatório.")
            .MaximumLength(100).WithMessage("O identificador do mecânico deve ter no máximo 100 caracteres.");

        RuleFor(x => x.Resumo)
            .NotEmpty().WithMessage("O resumo da execução é obrigatório.")
            .MaximumLength(2000).WithMessage("O resumo da execução deve ter no máximo 2000 caracteres.");
    }
}
