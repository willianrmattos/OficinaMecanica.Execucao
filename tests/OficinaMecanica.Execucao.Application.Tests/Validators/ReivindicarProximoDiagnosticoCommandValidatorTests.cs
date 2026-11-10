using FluentAssertions;
using OficinaMecanica.Execucao.Application.Commands.ReivindicarProximoDiagnostico;

namespace OficinaMecanica.Execucao.Application.Tests.Validators;

public class ReivindicarProximoDiagnosticoCommandValidatorTests
{
    private readonly ReivindicarProximoDiagnosticoCommandValidator _validator = new();

    [Fact]
    public async Task Validate_ComMecanico_DeveSerValido()
    {
        var result = await _validator.ValidateAsync(new ReivindicarProximoDiagnosticoCommand("mecanico-01"));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Validate_SemMecanico_DeveSerInvalido(string mecanicoId)
    {
        var result = await _validator.ValidateAsync(new ReivindicarProximoDiagnosticoCommand(mecanicoId));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "MecanicoId" && e.ErrorMessage == "O mecânico é obrigatório.");
    }

    [Fact]
    public async Task Validate_ComMecanicoMuitoLongo_DeveSerInvalido()
    {
        var result = await _validator.ValidateAsync(new ReivindicarProximoDiagnosticoCommand(new string('m', 101)));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "MecanicoId" && e.ErrorMessage.Contains("100 caracteres"));
    }
}
