using FluentValidation;
using OficinaMecanica.Contracts.Common;

namespace OficinaMecanica.Execucao.Application.Common;

internal sealed class ItemDeServicoMensagemValidator : AbstractValidator<ItemDeServicoMensagem>
{
    public ItemDeServicoMensagemValidator()
    {
        RuleFor(x => x.Nome).NotEmpty().WithMessage("O nome do serviço é obrigatório.");
        RuleFor(x => x.Quantidade).GreaterThan(0).WithMessage("A quantidade do serviço deve ser maior que zero.");
        RuleFor(x => x.PrecoUnitario).GreaterThanOrEqualTo(0).WithMessage("O preço do serviço não pode ser negativo.");
    }
}
