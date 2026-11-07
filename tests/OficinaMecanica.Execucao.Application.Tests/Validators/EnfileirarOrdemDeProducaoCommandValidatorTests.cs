using FluentAssertions;
using OficinaMecanica.Execucao.Application.Commands.EnfileirarOrdemDeProducao;

namespace OficinaMecanica.Execucao.Application.Tests.Validators;

public class EnfileirarOrdemDeProducaoCommandValidatorTests
{
    private readonly EnfileirarOrdemDeProducaoCommandValidator _validator = new();

    [Fact]
    public async Task Validate_ComDadosValidos_DeveSerValido()
    {
        var command = EnfileirarOrdemDeProducaoCommand.DaMensagem(new SolicitarDiagnosticoBuilder().Build());

        var result = await _validator.ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_SemPecas_DeveSerValido()
    {
        var command = EnfileirarOrdemDeProducaoCommand.DaMensagem(new SolicitarDiagnosticoBuilder().ComPecas().Build());

        var result = await _validator.ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_SemOrdemDeServico_DeveSerInvalido()
    {
        var command = EnfileirarOrdemDeProducaoCommand.DaMensagem(new SolicitarDiagnosticoBuilder().ComOrdemDeServicoId(Guid.Empty).Build());

        var result = await _validator.ValidateAsync(command);

        result.Errors.Should().ContainSingle(e => e.PropertyName == "OrdemDeServicoId" && e.ErrorMessage == "A ordem de serviço é obrigatória.");
    }

    [Theory]
    [InlineData("Numero", "O número da ordem é obrigatório.")]
    [InlineData("Placa", "A placa do veículo é obrigatória.")]
    [InlineData("Marca", "A marca do veículo é obrigatória.")]
    [InlineData("Modelo", "O modelo do veículo é obrigatório.")]
    public async Task Validate_ComCampoDeTextoVazio_DeveSerInvalido(string campo, string mensagem)
    {
        var valido = EnfileirarOrdemDeProducaoCommand.DaMensagem(new SolicitarDiagnosticoBuilder().Build());
        var command = campo switch
        {
            "Numero" => valido with { Numero = "" },
            "Placa" => valido with { Placa = "" },
            "Marca" => valido with { Marca = "" },
            _ => valido with { Modelo = "" }
        };

        var result = await _validator.ValidateAsync(command);

        result.Errors.Should().ContainSingle(e => e.PropertyName == campo && e.ErrorMessage == mensagem);
    }

    [Fact]
    public async Task Validate_SemServicos_DeveSerValidoParaNaoEnvenenarAFila()
    {
        var command = EnfileirarOrdemDeProducaoCommand.DaMensagem(new SolicitarDiagnosticoBuilder().ComServicos().Build());

        var result = await _validator.ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_SemObservacoes_DeveSerValido()
    {
        var command = EnfileirarOrdemDeProducaoCommand.DaMensagem(new SolicitarDiagnosticoBuilder().SemObservacoes().Build());

        var result = await _validator.ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ComObservacoesNoLimite_DeveSerValido()
    {
        var command = EnfileirarOrdemDeProducaoCommand.DaMensagem(new SolicitarDiagnosticoBuilder().Build()) with { Observacoes = new string('o', 1000) };

        var result = await _validator.ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_ComObservacoesAcimaDoLimite_DeveSerInvalido()
    {
        var command = EnfileirarOrdemDeProducaoCommand.DaMensagem(new SolicitarDiagnosticoBuilder().Build()) with { Observacoes = new string('o', 1001) };

        var result = await _validator.ValidateAsync(command);

        result.Errors.Should().ContainSingle(e =>
            e.PropertyName == "Observacoes" && e.ErrorMessage == "As observações devem ter no máximo 1000 caracteres.");
    }

    [Fact]
    public async Task Validate_ComListaDeServicosNula_DeveSerInvalido()
    {
        var command = EnfileirarOrdemDeProducaoCommand.DaMensagem(new SolicitarDiagnosticoBuilder().Build()) with { Servicos = null! };

        var result = await _validator.ValidateAsync(command);

        result.Errors.Should().ContainSingle(e => e.PropertyName == "Servicos");
    }

    [Fact]
    public async Task Validate_ComServicoInvalido_DeveApontarOItem()
    {
        var servico = new ItemDeServicoMensagemBuilder().ComNome("").ComQuantidade(0).ComPrecoUnitario(-1m).Build();
        var command = EnfileirarOrdemDeProducaoCommand.DaMensagem(new SolicitarDiagnosticoBuilder().ComServicos(servico).Build());

        var result = await _validator.ValidateAsync(command);

        result.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(
            "Servicos[0].Nome", "Servicos[0].Quantidade", "Servicos[0].PrecoUnitario");
    }

    [Fact]
    public async Task Validate_ComPecaInvalida_DeveApontarOItem()
    {
        var peca = new ItemDePecaMensagemBuilder().ComNome("").ComQuantidade(0).ComPrecoUnitario(-1m).Build();
        var command = EnfileirarOrdemDeProducaoCommand.DaMensagem(new SolicitarDiagnosticoBuilder().ComPecas(peca).Build());

        var result = await _validator.ValidateAsync(command);

        result.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(
            "Pecas[0].Nome", "Pecas[0].Quantidade", "Pecas[0].PrecoUnitario");
    }

    [Fact]
    public async Task Validate_ComListaDePecasNula_DeveSerInvalido()
    {
        var command = EnfileirarOrdemDeProducaoCommand.DaMensagem(new SolicitarDiagnosticoBuilder().Build()) with { Pecas = null! };

        var result = await _validator.ValidateAsync(command);

        result.Errors.Should().ContainSingle(e => e.PropertyName == "Pecas");
    }
}
