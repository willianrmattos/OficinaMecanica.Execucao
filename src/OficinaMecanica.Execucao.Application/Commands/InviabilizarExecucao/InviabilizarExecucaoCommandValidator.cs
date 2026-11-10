using FluentValidation;

namespace OficinaMecanica.Execucao.Application.Commands.InviabilizarExecucao;

public class InviabilizarExecucaoCommandValidator : AbstractValidator<InviabilizarExecucaoCommand>
{
    public InviabilizarExecucaoCommandValidator()
    {
        RuleFor(x => x.OrdemDeServicoId).NotEmpty().WithMessage("A ordem de serviço é obrigatória.");

        RuleFor(x => x.MecanicoId)
            .NotEmpty().WithMessage("O mecânico é obrigatório.")
            .MaximumLength(100).WithMessage("O identificador do mecânico deve ter no máximo 100 caracteres.");

        RuleFor(x => x.Motivo)
            .NotEmpty().WithMessage("O motivo da inviabilidade é obrigatório.")
            .MaximumLength(500).WithMessage("O motivo da inviabilidade deve ter no máximo 500 caracteres.");
    }
}
