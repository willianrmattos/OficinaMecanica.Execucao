using FluentValidation;
using OficinaMecanica.Execucao.Application.Common;

namespace OficinaMecanica.Execucao.Application.Commands.EnfileirarOrdemDeProducao;

public class EnfileirarOrdemDeProducaoCommandValidator : AbstractValidator<EnfileirarOrdemDeProducaoCommand>
{
    public EnfileirarOrdemDeProducaoCommandValidator()
    {
        RuleFor(x => x.OrdemDeServicoId).NotEmpty().WithMessage("A ordem de serviço é obrigatória.");
        RuleFor(x => x.Numero).NotEmpty().WithMessage("O número da ordem é obrigatório.");
        RuleFor(x => x.Placa).NotEmpty().WithMessage("A placa do veículo é obrigatória.");
        RuleFor(x => x.Marca).NotEmpty().WithMessage("A marca do veículo é obrigatória.");
        RuleFor(x => x.Modelo).NotEmpty().WithMessage("O modelo do veículo é obrigatório.");

        // Mesmo limite do monolito (1000), que e quem produz a observacao.
        RuleFor(x => x.Observacoes)
            .MaximumLength(1000).WithMessage("As observações devem ter no máximo 1000 caracteres.");

        RuleFor(x => x.Servicos).NotNull().WithMessage("A lista de serviços é obrigatória (pode ser vazia).");
        RuleForEach(x => x.Servicos).SetValidator(new ItemDeServicoMensagemValidator());

        RuleFor(x => x.Pecas).NotNull().WithMessage("A lista de peças é obrigatória (pode ser vazia).");
        RuleForEach(x => x.Pecas).SetValidator(new ItemDePecaMensagemValidator());
    }
}
