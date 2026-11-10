using FluentAssertions;
using FluentValidation;
using OficinaMecanica.Execucao.Application.Behaviors;
using OficinaMecanica.Execucao.Application.Commands.ReivindicarProximoDiagnostico;
using OficinaMecanica.Execucao.Application.DTOs;

namespace OficinaMecanica.Execucao.Application.Tests.Behaviors;

public class ValidationBehaviorTests
{
    private static readonly ResultadoDaReivindicacaoDto Resultado = ResultadoDaReivindicacaoDto.Vazia();

    [Fact]
    public async Task Handle_SemValidadores_DeveChamarOProximoDaPipeline()
    {
        var behavior = new ValidationBehavior<ReivindicarProximoDiagnosticoCommand, ResultadoDaReivindicacaoDto>(
            Array.Empty<IValidator<ReivindicarProximoDiagnosticoCommand>>());

        var resultado = await behavior.Handle(
            new ReivindicarProximoDiagnosticoCommand(""), () => Task.FromResult(Resultado), CancellationToken.None);

        resultado.Should().BeSameAs(Resultado);
    }

    [Fact]
    public async Task Handle_ComRequestValido_DeveChamarOProximoDaPipeline()
    {
        var behavior = new ValidationBehavior<ReivindicarProximoDiagnosticoCommand, ResultadoDaReivindicacaoDto>(
            new[] { new ReivindicarProximoDiagnosticoCommandValidator() });

        var resultado = await behavior.Handle(
            new ReivindicarProximoDiagnosticoCommand("mecanico-01"), () => Task.FromResult(Resultado), CancellationToken.None);

        resultado.Should().BeSameAs(Resultado);
    }

    [Fact]
    public async Task Handle_ComRequestInvalido_DeveLancarValidationExceptionSemChamarOProximo()
    {
        var proximoFoiChamado = false;
        var behavior = new ValidationBehavior<ReivindicarProximoDiagnosticoCommand, ResultadoDaReivindicacaoDto>(
            new[] { new ReivindicarProximoDiagnosticoCommandValidator() });

        var act = () => behavior.Handle(
            new ReivindicarProximoDiagnosticoCommand(""),
            () =>
            {
                proximoFoiChamado = true;
                return Task.FromResult(Resultado);
            },
            CancellationToken.None);

        var excecao = await act.Should().ThrowAsync<ValidationException>();
        excecao.Which.Errors.Should().ContainSingle(e => e.PropertyName == "MecanicoId");
        proximoFoiChamado.Should().BeFalse();
    }
}
