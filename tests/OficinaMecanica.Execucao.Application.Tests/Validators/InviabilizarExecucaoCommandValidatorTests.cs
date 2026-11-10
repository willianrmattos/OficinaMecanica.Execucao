using FluentAssertions;
using OficinaMecanica.Execucao.Application.Commands.InviabilizarExecucao;

namespace OficinaMecanica.Execucao.Application.Tests.Validators;

public class InviabilizarExecucaoCommandValidatorTests
{
    private readonly InviabilizarExecucaoCommandValidator _validator = new();

    [Fact]
    public async Task Validate_ComDadosValidos_DeveSerValido()
    {
        var result = await _validator.ValidateAsync(new InviabilizarExecucaoCommand(Guid.NewGuid(), "mecanico-01", "Peça indisponível"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_SemOrdemDeServico_DeveSerInvalido()
    {
        var result = await _validator.ValidateAsync(new InviabilizarExecucaoCommand(Guid.Empty, "mecanico-01", "Motivo"));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "OrdemDeServicoId" && e.ErrorMessage == "A ordem de serviço é obrigatória.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Validate_SemMecanico_DeveSerInvalido(string mecanicoId)
    {
        var result = await _validator.ValidateAsync(new InviabilizarExecucaoCommand(Guid.NewGuid(), mecanicoId, "Motivo"));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "MecanicoId" && e.ErrorMessage == "O mecânico é obrigatório.");
    }

    [Fact]
    public async Task Validate_ComMecanicoMuitoLongo_DeveSerInvalido()
    {
        var result = await _validator.ValidateAsync(new InviabilizarExecucaoCommand(Guid.NewGuid(), new string('m', 101), "Motivo"));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "MecanicoId" && e.ErrorMessage.Contains("100 caracteres"));
    }

    [Fact]
    public async Task Validate_SemMotivo_DeveSerInvalido()
    {
        var result = await _validator.ValidateAsync(new InviabilizarExecucaoCommand(Guid.NewGuid(), "mecanico-01", ""));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "Motivo" && e.ErrorMessage == "O motivo da inviabilidade é obrigatório.");
    }

    [Fact]
    public async Task Validate_ComMotivoMuitoLongo_DeveSerInvalido()
    {
        var result = await _validator.ValidateAsync(new InviabilizarExecucaoCommand(Guid.NewGuid(), "mecanico-01", new string('m', 501)));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "Motivo" && e.ErrorMessage.Contains("500 caracteres"));
    }
}
