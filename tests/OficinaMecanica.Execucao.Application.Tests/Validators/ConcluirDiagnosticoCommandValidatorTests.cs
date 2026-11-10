using FluentAssertions;
using OficinaMecanica.Execucao.Application.Commands.ConcluirDiagnostico;

namespace OficinaMecanica.Execucao.Application.Tests.Validators;

public class ConcluirDiagnosticoCommandValidatorTests
{
    private readonly ConcluirDiagnosticoCommandValidator _validator = new();

    [Fact]
    public async Task Validate_ComDadosValidos_DeveSerValido()
    {
        var result = await _validator.ValidateAsync(new ConcluirDiagnosticoCommand(Guid.NewGuid(), "mecanico-01", "Troca de óleo", 1.5m));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_SemHorasEstimadas_DeveSerValido()
    {
        var result = await _validator.ValidateAsync(new ConcluirDiagnosticoCommand(Guid.NewGuid(), "mecanico-01", "Troca de óleo"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_SemOrdemDeServico_DeveSerInvalido()
    {
        var result = await _validator.ValidateAsync(new ConcluirDiagnosticoCommand(Guid.Empty, "mecanico-01", "Laudo"));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "OrdemDeServicoId" && e.ErrorMessage == "A ordem de serviço é obrigatória.");
    }

    [Fact]
    public async Task Validate_SemMecanico_DeveSerInvalido()
    {
        var result = await _validator.ValidateAsync(new ConcluirDiagnosticoCommand(Guid.NewGuid(), "", "Laudo"));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "MecanicoId" && e.ErrorMessage == "O mecânico é obrigatório.");
    }

    [Fact]
    public async Task Validate_ComMecanicoMuitoLongo_DeveSerInvalido()
    {
        var result = await _validator.ValidateAsync(new ConcluirDiagnosticoCommand(Guid.NewGuid(), new string('m', 101), "Laudo"));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "MecanicoId" && e.ErrorMessage.Contains("100 caracteres"));
    }

    [Fact]
    public async Task Validate_SemLaudo_DeveSerInvalido()
    {
        var result = await _validator.ValidateAsync(new ConcluirDiagnosticoCommand(Guid.NewGuid(), "mecanico-01", ""));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "Laudo" && e.ErrorMessage == "O laudo é obrigatório.");
    }

    [Fact]
    public async Task Validate_ComLaudoMuitoLongo_DeveSerInvalido()
    {
        var result = await _validator.ValidateAsync(new ConcluirDiagnosticoCommand(Guid.NewGuid(), "mecanico-01", new string('l', 4001)));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "Laudo" && e.ErrorMessage.Contains("4000 caracteres"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Validate_ComHorasEstimadasNaoPositivas_DeveSerInvalido(int horas)
    {
        var result = await _validator.ValidateAsync(new ConcluirDiagnosticoCommand(Guid.NewGuid(), "mecanico-01", "Laudo", horas));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "HorasEstimadas" && e.ErrorMessage == "As horas estimadas devem ser maiores que zero.");
    }
}
