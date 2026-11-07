using FluentAssertions;
using OficinaMecanica.Execucao.Application.Commands.RegistrarApontamento;

namespace OficinaMecanica.Execucao.Application.Tests.Validators;

public class RegistrarApontamentoCommandValidatorTests
{
    private readonly RegistrarApontamentoCommandValidator _validator = new();

    [Fact]
    public async Task Validate_ComDadosValidos_DeveSerValido()
    {
        var result = await _validator.ValidateAsync(new RegistrarApontamentoCommand(Guid.NewGuid(), "mecanico-01", "Ruído no cubo"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_SemOrdemDeServico_DeveSerInvalido()
    {
        var result = await _validator.ValidateAsync(new RegistrarApontamentoCommand(Guid.Empty, "mecanico-01", "texto"));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "OrdemDeServicoId" && e.ErrorMessage == "A ordem de serviço é obrigatória.");
    }

    [Fact]
    public async Task Validate_SemMecanico_DeveSerInvalido()
    {
        var result = await _validator.ValidateAsync(new RegistrarApontamentoCommand(Guid.NewGuid(), "", "texto"));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "MecanicoId" && e.ErrorMessage == "O mecânico é obrigatório.");
    }

    [Fact]
    public async Task Validate_ComMecanicoMuitoLongo_DeveSerInvalido()
    {
        var result = await _validator.ValidateAsync(new RegistrarApontamentoCommand(Guid.NewGuid(), new string('m', 101), "texto"));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "MecanicoId" && e.ErrorMessage.Contains("100 caracteres"));
    }

    [Fact]
    public async Task Validate_SemTexto_DeveSerInvalido()
    {
        var result = await _validator.ValidateAsync(new RegistrarApontamentoCommand(Guid.NewGuid(), "mecanico-01", ""));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "Texto" && e.ErrorMessage == "O texto do apontamento é obrigatório.");
    }

    [Fact]
    public async Task Validate_ComTextoMuitoLongo_DeveSerInvalido()
    {
        var result = await _validator.ValidateAsync(new RegistrarApontamentoCommand(Guid.NewGuid(), "mecanico-01", new string('t', 2001)));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "Texto" && e.ErrorMessage.Contains("2000 caracteres"));
    }
}
