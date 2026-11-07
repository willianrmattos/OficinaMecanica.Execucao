using FluentValidation;

namespace OficinaMecanica.Execucao.Application.Queries.ListarFila;

public class ListarFilaQueryValidator : AbstractValidator<ListarFilaQuery>
{
    public ListarFilaQueryValidator()
    {
        RuleFor(x => x.Pagina).GreaterThanOrEqualTo(1).WithMessage("A página deve ser maior ou igual a 1.");

        RuleFor(x => x.TamanhoPagina)
            .InclusiveBetween(1, 100).WithMessage("O tamanho da página deve estar entre 1 e 100.");

        RuleFor(x => x.Status)
            .IsInEnum().When(x => x.Status.HasValue)
            .WithMessage("O status informado é inválido.");
    }
}
