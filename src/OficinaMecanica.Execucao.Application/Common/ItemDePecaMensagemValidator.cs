using FluentValidation;
using OficinaMecanica.Contracts.Common;

namespace OficinaMecanica.Execucao.Application.Common;

internal sealed class ItemDePecaMensagemValidator : AbstractValidator<ItemDePecaMensagem>
{
    public ItemDePecaMensagemValidator()
    {
        RuleFor(x => x.Nome).NotEmpty().WithMessage("O nome da peça é obrigatório.");
        RuleFor(x => x.Quantidade).GreaterThan(0).WithMessage("A quantidade da peça deve ser maior que zero.");
        RuleFor(x => x.PrecoUnitario).GreaterThanOrEqualTo(0).WithMessage("O preço da peça não pode ser negativo.");
    }
}
