using FluentAssertions;
using OficinaMecanica.Execucao.Application.Commands.CancelarProducao;

namespace OficinaMecanica.Execucao.Application.Tests.Validators;

public class CancelarProducaoCommandValidatorTests
{
    private readonly CancelarProducaoCommandValidator _validator = new();

    [Fact]
    public async Task Validate_ComDadosValidos_DeveSerValido()
    {
        var result = await _validator.ValidateAsync(new CancelarProducaoCommand(Guid.NewGuid(), "Pagamento cancelado"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_SemOrdemDeServico_DeveSerInvalido()
    {
        var result = await _validator.ValidateAsync(new CancelarProducaoCommand(Guid.Empty, "Motivo"));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "OrdemDeServicoId" && e.ErrorMessage == "A ordem de serviço é obrigatória.");
    }

    [Fact]
    public async Task Validate_SemMotivo_DeveSerInvalido()
    {
        var result = await _validator.ValidateAsync(new CancelarProducaoCommand(Guid.NewGuid(), ""));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "Motivo" && e.ErrorMessage == "O motivo do cancelamento é obrigatório.");
    }

    [Fact]
    public async Task Validate_ComMotivoMuitoLongo_DeveSerInvalido()
    {
        var result = await _validator.ValidateAsync(new CancelarProducaoCommand(Guid.NewGuid(), new string('m', 501)));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "Motivo" && e.ErrorMessage.Contains("500 caracteres"));
    }
}
