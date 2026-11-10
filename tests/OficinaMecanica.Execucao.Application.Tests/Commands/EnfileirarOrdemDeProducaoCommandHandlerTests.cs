using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OficinaMecanica.Execucao.Application.Commands.EnfileirarOrdemDeProducao;
using OficinaMecanica.Execucao.Domain.Entities;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Domain.Exceptions;
using OficinaMecanica.Execucao.Domain.Interfaces;
using OficinaMecanica.Execucao.Tests.Common;

namespace OficinaMecanica.Execucao.Application.Tests.Commands;

public class EnfileirarOrdemDeProducaoCommandHandlerTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IOrdemDeProducaoRepository> _repositorioMock = new();
    private readonly Mock<ICancelamentoPrevioRepository> _cancelamentoPrevioRepositorioMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly EnfileirarOrdemDeProducaoCommandHandler _handler;

    public EnfileirarOrdemDeProducaoCommandHandlerTests()
    {
        _repositorioMock
            .Setup(r => r.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrdemDeProducao?)null);
        _repositorioMock
            .Setup(r => r.AdicionarAsync(It.IsAny<OrdemDeProducao>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _cancelamentoPrevioRepositorioMock
            .Setup(r => r.ObterAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CancelamentoPrevio?)null);

        _handler = new EnfileirarOrdemDeProducaoCommandHandler(
            _repositorioMock.Object,
            _cancelamentoPrevioRepositorioMock.Object,
            _unitOfWorkMock.Object,
            new RelogioFixo(Agora),
            NullLogger<EnfileirarOrdemDeProducaoCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_OrdemNova_DeveEnfileirarNaFilaESalvar()
    {
        var mensagem = new SolicitarDiagnosticoBuilder().Build();
        OrdemDeProducao? adicionada = null;
        _repositorioMock
            .Setup(r => r.AdicionarAsync(It.IsAny<OrdemDeProducao>(), It.IsAny<CancellationToken>()))
            .Callback<OrdemDeProducao, CancellationToken>((ordem, _) => adicionada = ordem)
            .ReturnsAsync(true);

        await _handler.Handle(EnfileirarOrdemDeProducaoCommand.DaMensagem(mensagem), CancellationToken.None);

        adicionada.Should().NotBeNull();
        adicionada!.Id.Should().Be(mensagem.OrdemDeServicoId);
        adicionada.Numero.Should().Be(mensagem.Numero);
        adicionada.Status.Should().Be(StatusProducao.Fila);
        adicionada.EnfileiradaEm.Should().Be(Agora);
        adicionada.Veiculo.Placa.Should().Be(mensagem.Placa);
        adicionada.Veiculo.Marca.Should().Be(mensagem.Marca);
        adicionada.Veiculo.Modelo.Should().Be(mensagem.Modelo);
        adicionada.Observacoes.Should().Be(mensagem.Observacoes);
        adicionada.Servicos.Select(s => s.Nome).Should().Equal(mensagem.Servicos.Select(s => s.Nome));
        adicionada.Pecas.Select(p => p.Nome).Should().Equal(mensagem.Pecas.Select(p => p.Nome));
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_SemObservacoes_DeveEnfileirarComObservacoesNulas()
    {
        var mensagem = new SolicitarDiagnosticoBuilder().SemObservacoes().Build();

        await _handler.Handle(EnfileirarOrdemDeProducaoCommand.DaMensagem(mensagem), CancellationToken.None);

        _repositorioMock.Verify(
            r => r.AdicionarAsync(It.Is<OrdemDeProducao>(o => o.Observacoes == null), It.IsAny<CancellationToken>()),
            Times.Once());
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_SemPecas_DeveEnfileirarMesmoAssim()
    {
        var mensagem = new SolicitarDiagnosticoBuilder().ComPecas().Build();

        await _handler.Handle(EnfileirarOrdemDeProducaoCommand.DaMensagem(mensagem), CancellationToken.None);

        _repositorioMock.Verify(
            r => r.AdicionarAsync(It.Is<OrdemDeProducao>(o => o.Pecas.Count == 0), It.IsAny<CancellationToken>()),
            Times.Once());
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_SemServicos_DeveEnfileirarSemLancarExcecaoDeNegocio()
    {
        var mensagem = new SolicitarDiagnosticoBuilder().ComServicos().Build();

        var act = () => _handler.Handle(EnfileirarOrdemDeProducaoCommand.DaMensagem(mensagem), CancellationToken.None);

        await act.Should().NotThrowAsync();
        _repositorioMock.Verify(
            r => r.AdicionarAsync(It.Is<OrdemDeProducao>(o => o.Servicos.Count == 0), It.IsAny<CancellationToken>()),
            Times.Once());
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_OrdemJaEnfileirada_DeveSerIdempotenteSemAdicionarNemSalvar()
    {
        var existente = new OrdemDeProducaoBuilder().EmDiagnostico();
        _repositorioMock
            .Setup(r => r.ObterPorIdAsync(existente.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existente);
        var mensagem = new SolicitarDiagnosticoBuilder().ComOrdemDeServicoId(existente.Id).Build();

        var act = () => _handler.Handle(EnfileirarOrdemDeProducaoCommand.DaMensagem(mensagem), CancellationToken.None);

        await act.Should().NotThrowAsync();
        _repositorioMock.Verify(r => r.AdicionarAsync(It.IsAny<OrdemDeProducao>(), It.IsAny<CancellationToken>()), Times.Never());
        _cancelamentoPrevioRepositorioMock.Verify(r => r.ObterAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never());
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_CancelamentoChegouAntes_NaoDeveEnfileirarNemSalvar()
    {
        var mensagem = new SolicitarDiagnosticoBuilder().Build();
        var tombstone = new CancelamentoPrevioBuilder().DaOrdemDeServico(mensagem.OrdemDeServicoId).Build();
        _cancelamentoPrevioRepositorioMock
            .Setup(r => r.ObterAsync(mensagem.OrdemDeServicoId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tombstone);

        var act = () => _handler.Handle(EnfileirarOrdemDeProducaoCommand.DaMensagem(mensagem), CancellationToken.None);

        await act.Should().NotThrowAsync();
        _repositorioMock.Verify(r => r.AdicionarAsync(It.IsAny<OrdemDeProducao>(), It.IsAny<CancellationToken>()), Times.Never());
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_EntregaConcorrenteVenceAInsercao_DeveDescartarSemSalvar()
    {
        _repositorioMock
            .Setup(r => r.AdicionarAsync(It.IsAny<OrdemDeProducao>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var mensagem = new SolicitarDiagnosticoBuilder().Build();

        var act = () => _handler.Handle(EnfileirarOrdemDeProducaoCommand.DaMensagem(mensagem), CancellationToken.None);

        await act.Should().NotThrowAsync();
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_ComItemInvalido_DeveLancarDomainException()
    {
        var servicoInvalido = new ItemDeServicoMensagemBuilder().ComQuantidade(0).Build();
        var mensagem = new SolicitarDiagnosticoBuilder().ComServicos(servicoInvalido).Build();

        var act = () => _handler.Handle(EnfileirarOrdemDeProducaoCommand.DaMensagem(mensagem), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*quantidade*");
        _repositorioMock.Verify(r => r.AdicionarAsync(It.IsAny<OrdemDeProducao>(), It.IsAny<CancellationToken>()), Times.Never());
    }
}
