using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OficinaMecanica.Execucao.Application.Commands.LiberarParaExecucao;
using OficinaMecanica.Execucao.Domain.Entities;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Domain.Exceptions;
using OficinaMecanica.Execucao.Domain.Interfaces;
using OficinaMecanica.Execucao.Tests.Common;

namespace OficinaMecanica.Execucao.Application.Tests.Commands;

public class LiberarParaExecucaoCommandHandlerTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IOrdemDeProducaoRepository> _repositorioMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly LiberarParaExecucaoCommandHandler _handler;

    public LiberarParaExecucaoCommandHandlerTests()
    {
        _handler = new LiberarParaExecucaoCommandHandler(
            _repositorioMock.Object,
            _unitOfWorkMock.Object,
            new RelogioFixo(Agora),
            NullLogger<LiberarParaExecucaoCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_OrdemComDiagnosticoConcluido_DeveLiberarAtualizarESalvar()
    {
        var ordem = new OrdemDeProducaoBuilder().DiagnosticoConcluido();
        OrdemExiste(ordem);
        var mensagem = new IniciarExecucaoBuilder().ComOrdemDeServicoId(ordem.Id).Build();

        await _handler.Handle(LiberarParaExecucaoCommand.DaMensagem(mensagem), CancellationToken.None);

        ordem.Status.Should().Be(StatusProducao.AguardandoExecucao);
        ordem.LiberadaEm.Should().Be(Agora);
        _repositorioMock.Verify(r => r.AtualizarAsync(ordem, It.IsAny<CancellationToken>()), Times.Once());
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Handle_DeveAplicarOSnapshotAprovadoDeServicosEPecas()
    {
        var ordem = new OrdemDeProducaoBuilder().ComServicos(new ServicoDaOrdemBuilder().ComNome("Original").Build()).DiagnosticoConcluido();
        OrdemExiste(ordem);
        var servicoAprovado = new ItemDeServicoMensagemBuilder().ComNome("Troca de óleo e filtro").ComQuantidade(2).ComPrecoUnitario(150m).Build();
        var pecaAprovada = new ItemDePecaMensagemBuilder().ComNome("Filtro de óleo").ComQuantidade(3).ComPrecoUnitario(35.90m).Build();
        var mensagem = new IniciarExecucaoBuilder()
            .ComOrdemDeServicoId(ordem.Id)
            .ComServicos(servicoAprovado)
            .ComPecas(pecaAprovada)
            .Build();

        await _handler.Handle(LiberarParaExecucaoCommand.DaMensagem(mensagem), CancellationToken.None);

        var servico = ordem.Servicos.Should().ContainSingle().Subject;
        servico.ServicoId.Should().Be(servicoAprovado.ServicoId);
        servico.Nome.Should().Be("Troca de óleo e filtro");
        servico.Quantidade.Should().Be(2);
        servico.PrecoUnitario.Should().Be(150m);
        var peca = ordem.Pecas.Should().ContainSingle().Subject;
        peca.PecaId.Should().Be(pecaAprovada.PecaId);
        peca.Nome.Should().Be("Filtro de óleo");
        peca.Quantidade.Should().Be(3);
        peca.PrecoUnitario.Should().Be(35.90m);
    }

    [Fact]
    public async Task Handle_Reentrega_DeveSerIdempotenteSemAtualizarNemSalvar()
    {
        var ordem = new OrdemDeProducaoBuilder().AguardandoExecucao();
        var servicosOriginais = ordem.Servicos.ToList();
        OrdemExiste(ordem);
        var mensagem = new IniciarExecucaoBuilder().ComOrdemDeServicoId(ordem.Id).Build();

        var act = () => _handler.Handle(LiberarParaExecucaoCommand.DaMensagem(mensagem), CancellationToken.None);

        await act.Should().NotThrowAsync();
        ordem.Status.Should().Be(StatusProducao.AguardandoExecucao);
        ordem.Servicos.Should().Equal(servicosOriginais);
        _repositorioMock.Verify(r => r.AtualizarAsync(It.IsAny<OrdemDeProducao>(), It.IsAny<CancellationToken>()), Times.Never());
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    [Theory]
    [InlineData(StatusProducao.EmExecucao)]
    [InlineData(StatusProducao.Finalizada)]
    [InlineData(StatusProducao.Inviavel)]
    public async Task Handle_ReentregaTardiaDepoisDeAvancar_DeveSerIdempotenteSemAtualizarNemSalvar(StatusProducao status)
    {
        var ordem = new OrdemDeProducaoBuilder().NoStatus(status);
        var servicosAtuais = ordem.Servicos.ToList();
        OrdemExiste(ordem);
        var mensagem = new IniciarExecucaoBuilder().ComOrdemDeServicoId(ordem.Id).Build();

        var act = () => _handler.Handle(LiberarParaExecucaoCommand.DaMensagem(mensagem), CancellationToken.None);

        await act.Should().NotThrowAsync();
        ordem.Status.Should().Be(status);
        ordem.Servicos.Should().Equal(servicosAtuais);
        _repositorioMock.Verify(r => r.AtualizarAsync(It.IsAny<OrdemDeProducao>(), It.IsAny<CancellationToken>()), Times.Never());
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_OrdemInexistente_DeveLancarKeyNotFound()
    {
        _repositorioMock
            .Setup(r => r.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrdemDeProducao?)null);
        var mensagem = new IniciarExecucaoBuilder().Build();

        var act = () => _handler.Handle(LiberarParaExecucaoCommand.DaMensagem(mensagem), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*não encontrada*");
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_OrdemAindaNaoDiagnosticada_DeveLancarDomainExceptionSemSalvar()
    {
        var ordem = new OrdemDeProducaoBuilder().EmDiagnostico();
        OrdemExiste(ordem);
        var mensagem = new IniciarExecucaoBuilder().ComOrdemDeServicoId(ordem.Id).Build();

        var act = () => _handler.Handle(LiberarParaExecucaoCommand.DaMensagem(mensagem), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*liberar para execução*");
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_OrdemCancelada_DeveLancarDomainExceptionSemSalvar()
    {
        var ordem = new OrdemDeProducaoBuilder().Cancelada();
        OrdemExiste(ordem);
        var mensagem = new IniciarExecucaoBuilder().ComOrdemDeServicoId(ordem.Id).Build();

        var act = () => _handler.Handle(LiberarParaExecucaoCommand.DaMensagem(mensagem), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*liberar para execução*");
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Handle_SnapshotSemServicos_DeveLancarDomainExceptionSemSalvar()
    {
        var ordem = new OrdemDeProducaoBuilder().DiagnosticoConcluido();
        OrdemExiste(ordem);
        var mensagem = new IniciarExecucaoBuilder().ComOrdemDeServicoId(ordem.Id).ComServicos().Build();

        var act = () => _handler.Handle(LiberarParaExecucaoCommand.DaMensagem(mensagem), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*pelo menos um serviço*");
        ordem.Status.Should().Be(StatusProducao.DiagnosticoConcluido);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    private void OrdemExiste(OrdemDeProducao ordem) =>
        _repositorioMock
            .Setup(r => r.ObterPorIdAsync(ordem.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ordem);
}
