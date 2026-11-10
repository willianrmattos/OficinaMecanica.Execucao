using FluentValidation;

namespace OficinaMecanica.Execucao.Application.Commands.RegistrarApontamento;

public class RegistrarApontamentoCommandValidator : AbstractValidator<RegistrarApontamentoCommand>
{
    public RegistrarApontamentoCommandValidator()
    {
        RuleFor(x => x.OrdemDeServicoId).NotEmpty().WithMessage("A ordem de serviço é obrigatória.");

        RuleFor(x => x.MecanicoId)
            .NotEmpty().WithMessage("O mecânico é obrigatório.")
            .MaximumLength(100).WithMessage("O identificador do mecânico deve ter no máximo 100 caracteres.");

        RuleFor(x => x.Texto)
            .NotEmpty().WithMessage("O texto do apontamento é obrigatório.")
            .MaximumLength(2000).WithMessage("O texto do apontamento deve ter no máximo 2000 caracteres.");
    }
}
