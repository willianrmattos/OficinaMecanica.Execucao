using FluentAssertions;
using OficinaMecanica.Execucao.Application.Commands.ReivindicarProximoReparo;

namespace OficinaMecanica.Execucao.Application.Tests.Validators;

public class ReivindicarProximoReparoCommandValidatorTests
{
    private readonly ReivindicarProximoReparoCommandValidator _validator = new();

    [Fact]
    public async Task Validate_ComMecanico_DeveSerValido()
    {
        var result = await _validator.ValidateAsync(new ReivindicarProximoReparoCommand("mecanico-01"));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Validate_SemMecanico_DeveSerInvalido(string mecanicoId)
    {
        var result = await _validator.ValidateAsync(new ReivindicarProximoReparoCommand(mecanicoId));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "MecanicoId" && e.ErrorMessage == "O mecânico é obrigatório.");
    }

    [Fact]
    public async Task Validate_ComMecanicoMuitoLongo_DeveSerInvalido()
    {
        var result = await _validator.ValidateAsync(new ReivindicarProximoReparoCommand(new string('m', 101)));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "MecanicoId" && e.ErrorMessage.Contains("100 caracteres"));
    }
}
