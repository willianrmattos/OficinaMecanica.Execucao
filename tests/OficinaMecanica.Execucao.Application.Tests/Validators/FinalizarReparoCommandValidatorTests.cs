using FluentAssertions;
using OficinaMecanica.Execucao.Application.Commands.FinalizarReparo;

namespace OficinaMecanica.Execucao.Application.Tests.Validators;

public class FinalizarReparoCommandValidatorTests
{
    private readonly FinalizarReparoCommandValidator _validator = new();

    [Fact]
    public async Task Validate_ComDadosValidos_DeveSerValido()
    {
        var result = await _validator.ValidateAsync(new FinalizarReparoCommand(Guid.NewGuid(), "mecanico-01", "Serviço concluído"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_SemOrdemDeServico_DeveSerInvalido()
    {
        var result = await _validator.ValidateAsync(new FinalizarReparoCommand(Guid.Empty, "mecanico-01", "Resumo"));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "OrdemDeServicoId" && e.ErrorMessage == "A ordem de serviço é obrigatória.");
    }

    [Fact]
    public async Task Validate_SemMecanico_DeveSerInvalido()
    {
        var result = await _validator.ValidateAsync(new FinalizarReparoCommand(Guid.NewGuid(), "", "Resumo"));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "MecanicoId" && e.ErrorMessage == "O mecânico é obrigatório.");
    }

    [Fact]
    public async Task Validate_ComMecanicoMuitoLongo_DeveSerInvalido()
    {
        var result = await _validator.ValidateAsync(new FinalizarReparoCommand(Guid.NewGuid(), new string('m', 101), "Resumo"));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "MecanicoId" && e.ErrorMessage.Contains("100 caracteres"));
    }

    [Fact]
    public async Task Validate_SemResumo_DeveSerInvalido()
    {
        var result = await _validator.ValidateAsync(new FinalizarReparoCommand(Guid.NewGuid(), "mecanico-01", ""));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "Resumo" && e.ErrorMessage == "O resumo da execução é obrigatório.");
    }

    [Fact]
    public async Task Validate_ComResumoMuitoLongo_DeveSerInvalido()
    {
        var result = await _validator.ValidateAsync(new FinalizarReparoCommand(Guid.NewGuid(), "mecanico-01", new string('r', 2001)));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "Resumo" && e.ErrorMessage.Contains("2000 caracteres"));
    }
}
