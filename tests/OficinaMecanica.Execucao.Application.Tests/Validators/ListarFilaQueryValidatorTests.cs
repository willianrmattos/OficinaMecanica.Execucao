using FluentAssertions;
using OficinaMecanica.Execucao.Application.Queries.ListarFila;
using OficinaMecanica.Execucao.Domain.Enums;

namespace OficinaMecanica.Execucao.Application.Tests.Validators;

public class ListarFilaQueryValidatorTests
{
    private readonly ListarFilaQueryValidator _validator = new();

    [Fact]
    public async Task Validate_ComValoresPadrao_DeveSerValido()
    {
        var result = await _validator.ValidateAsync(new ListarFilaQuery());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ComFiltros_DeveSerValido()
    {
        var result = await _validator.ValidateAsync(new ListarFilaQuery(StatusProducao.Fila, "mecanico-01", 2, 100));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Validate_ComPaginaMenorQueUm_DeveSerInvalido(int pagina)
    {
        var result = await _validator.ValidateAsync(new ListarFilaQuery(Pagina: pagina));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "Pagina" && e.ErrorMessage == "A página deve ser maior ou igual a 1.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Validate_ComTamanhoDePaginaForaDoLimite_DeveSerInvalido(int tamanho)
    {
        var result = await _validator.ValidateAsync(new ListarFilaQuery(TamanhoPagina: tamanho));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "TamanhoPagina" && e.ErrorMessage == "O tamanho da página deve estar entre 1 e 100.");
    }

    [Fact]
    public async Task Validate_ComStatusForaDoEnum_DeveSerInvalido()
    {
        var result = await _validator.ValidateAsync(new ListarFilaQuery(Status: (StatusProducao)999));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "Status" && e.ErrorMessage == "O status informado é inválido.");
    }
}
