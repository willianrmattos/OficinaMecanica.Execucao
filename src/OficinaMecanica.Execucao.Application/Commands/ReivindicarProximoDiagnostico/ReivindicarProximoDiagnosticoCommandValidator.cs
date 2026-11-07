using FluentValidation;

namespace OficinaMecanica.Execucao.Application.Commands.ReivindicarProximoDiagnostico;

public class ReivindicarProximoDiagnosticoCommandValidator : AbstractValidator<ReivindicarProximoDiagnosticoCommand>
{
    public ReivindicarProximoDiagnosticoCommandValidator()
    {
        RuleFor(x => x.MecanicoId)
            .NotEmpty().WithMessage("O mecânico é obrigatório.")
            .MaximumLength(100).WithMessage("O identificador do mecânico deve ter no máximo 100 caracteres.");
    }
}
