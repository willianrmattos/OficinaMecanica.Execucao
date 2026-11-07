using FluentAssertions;
using OficinaMecanica.Execucao.Application.Commands.LiberarParaExecucao;

namespace OficinaMecanica.Execucao.Application.Tests.Validators;

public class LiberarParaExecucaoCommandValidatorTests
{
    private readonly LiberarParaExecucaoCommandValidator _validator = new();

    [Fact]
    public async Task Validate_ComSnapshotValido_DeveSerValido()
    {
        var command = LiberarParaExecucaoCommand.DaMensagem(new IniciarExecucaoBuilder().Build());

        var result = await _validator.ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_SemPecas_DeveSerValido()
    {
        var command = LiberarParaExecucaoCommand.DaMensagem(new IniciarExecucaoBuilder().ComPecas().Build());

        var result = await _validator.ValidateAsync(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_SemOrdemDeServico_DeveSerInvalido()
    {
        var command = LiberarParaExecucaoCommand.DaMensagem(new IniciarExecucaoBuilder().ComOrdemDeServicoId(Guid.Empty).Build());

        var result = await _validator.ValidateAsync(command);

        result.Errors.Should().ContainSingle(e => e.PropertyName == "OrdemDeServicoId" && e.ErrorMessage == "A ordem de serviço é obrigatória.");
    }

    [Fact]
    public async Task Validate_SemServicos_DeveSerInvalido()
    {
        var command = LiberarParaExecucaoCommand.DaMensagem(new IniciarExecucaoBuilder().ComServicos().Build());

        var result = await _validator.ValidateAsync(command);

        result.Errors.Should().ContainSingle(e => e.PropertyName == "Servicos" && e.ErrorMessage == "O snapshot aprovado deve ter pelo menos um serviço.");
    }

    [Fact]
    public async Task Validate_ComServicoInvalido_DeveApontarOItem()
    {
        var servico = new ItemDeServicoMensagemBuilder().ComQuantidade(0).Build();
        var command = LiberarParaExecucaoCommand.DaMensagem(new IniciarExecucaoBuilder().ComServicos(servico).Build());

        var result = await _validator.ValidateAsync(command);

        result.Errors.Should().ContainSingle(e => e.PropertyName == "Servicos[0].Quantidade");
    }

    [Fact]
    public async Task Validate_ComPecaInvalida_DeveApontarOItem()
    {
        var peca = new ItemDePecaMensagemBuilder().ComPrecoUnitario(-5m).Build();
        var command = LiberarParaExecucaoCommand.DaMensagem(new IniciarExecucaoBuilder().ComPecas(peca).Build());

        var result = await _validator.ValidateAsync(command);

        result.Errors.Should().ContainSingle(e => e.PropertyName == "Pecas[0].PrecoUnitario");
    }

    [Fact]
    public async Task Validate_ComListaDePecasNula_DeveSerInvalido()
    {
        var command = LiberarParaExecucaoCommand.DaMensagem(new IniciarExecucaoBuilder().Build()) with { Pecas = null! };

        var result = await _validator.ValidateAsync(command);

        result.Errors.Should().ContainSingle(e => e.PropertyName == "Pecas");
    }
}
