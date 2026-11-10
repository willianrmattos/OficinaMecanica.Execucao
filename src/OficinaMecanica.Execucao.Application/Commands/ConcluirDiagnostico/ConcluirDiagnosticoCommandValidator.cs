using FluentValidation;

namespace OficinaMecanica.Execucao.Application.Commands.ConcluirDiagnostico;

public class ConcluirDiagnosticoCommandValidator : AbstractValidator<ConcluirDiagnosticoCommand>
{
    public ConcluirDiagnosticoCommandValidator()
    {
        RuleFor(x => x.OrdemDeServicoId).NotEmpty().WithMessage("A ordem de serviço é obrigatória.");

        RuleFor(x => x.MecanicoId)
            .NotEmpty().WithMessage("O mecânico é obrigatório.")
            .MaximumLength(100).WithMessage("O identificador do mecânico deve ter no máximo 100 caracteres.");

        RuleFor(x => x.Laudo)
            .NotEmpty().WithMessage("O laudo é obrigatório.")
            .MaximumLength(4000).WithMessage("O laudo deve ter no máximo 4000 caracteres.");

        RuleFor(x => x.HorasEstimadas)
            .GreaterThan(0).When(x => x.HorasEstimadas.HasValue)
            .WithMessage("As horas estimadas devem ser maiores que zero.");
    }
}
