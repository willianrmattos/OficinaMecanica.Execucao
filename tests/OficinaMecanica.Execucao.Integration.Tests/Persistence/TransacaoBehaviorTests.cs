using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using OficinaMecanica.Execucao.Application.Commands.ConcluirDiagnostico;
using OficinaMecanica.Execucao.Application.DTOs;
using OficinaMecanica.Execucao.Application.Queries.ObterResumoDaFila;
using OficinaMecanica.Execucao.Domain.Exceptions;
using OficinaMecanica.Execucao.Infrastructure.Observability;
using OficinaMecanica.Execucao.Infrastructure.Persistence;
using OficinaMecanica.Execucao.Integration.Tests.Support;

namespace OficinaMecanica.Execucao.Integration.Tests.Persistence;

public class TransacaoBehaviorTests
{
    // Transacao em memoria que so registra o ciclo de vida: o comportamento do Mongo e coberto nos testes de infra.
    private sealed class TransacaoFalsa : ITransacaoMongo
    {
        public bool EhAmbiente { get; set; }
        public bool Aberta { get; private set; }
        public int Iniciadas { get; private set; }
        public int Confirmadas { get; private set; }
        public int Abortadas { get; private set; }

        public IClientSessionHandle? Current => null;
        public bool Ambiente => EhAmbiente;

        public Task IniciarAsync(CancellationToken cancellationToken)
        {
            Aberta = true;
            Iniciadas++;
            return Task.CompletedTask;
        }

        public Task ConfirmarAsync(CancellationToken cancellationToken)
        {
            if (Aberta) Confirmadas++;
            Aberta = false;
            return Task.CompletedTask;
        }

        public Task AbortarAsync()
        {
            if (Aberta) Abortadas++;
            Aberta = false;
            return Task.CompletedTask;
        }
    }

    private static readonly ConcluirDiagnosticoCommand Comando = new(Guid.NewGuid(), "m1", "laudo");
    private static readonly OrdemDeProducaoDto Resposta = OrdemDeProducaoMapper.ToDto(new OrdemDeProducaoBuilder().NaFila());

    private readonly TransacaoFalsa _transacao = new();
    private readonly MetricasPendentes _metricas = new();

    private TransacaoBehavior<TRequest, TResponse> Criar<TRequest, TResponse>(int maximo = 3) where TRequest : notnull =>
        new(_transacao, _metricas, new TransacaoOptions { MaximoDeTentativas = maximo, AtrasoBaseEmMs = 1 },
            NullLogger<TransacaoBehavior<TRequest, TResponse>>.Instance);

    private static MongoException ConflitoDeTransacao()
    {
        var excecao = new MongoException("WriteConflict");
        excecao.AddErrorLabel("TransientTransactionError");
        return excecao;
    }

    [Fact]
    public async Task Query_NaoDeveAbrirTransacao()
    {
        var comportamento = Criar<ObterResumoDaFilaQuery, ResumoDaFilaDto>();
        var resumo = new ResumoDaFilaDto(new Dictionary<string, int>(), 0);

        var resultado = await comportamento.Handle(new ObterResumoDaFilaQuery(), () => Task.FromResult(resumo), CancellationToken.None);

        resultado.Should().BeSameAs(resumo);
        _transacao.Iniciadas.Should().Be(0);
    }

    [Fact]
    public async Task CommandDentroDeConsumer_DeveRepassarSemAbrirNemAbortarATransacaoDoOutbox()
    {
        _transacao.EhAmbiente = true;
        var comportamento = Criar<ConcluirDiagnosticoCommand, OrdemDeProducaoDto>();

        await comportamento.Handle(Comando, () => Task.FromResult(Resposta), CancellationToken.None);

        _transacao.Iniciadas.Should().Be(0);
        _transacao.Abortadas.Should().Be(0);
    }

    [Fact]
    public async Task CommandHttp_QueConfirmaNoSaveChanges_DeveAbrirUmaVezESemAbortar()
    {
        var comportamento = Criar<ConcluirDiagnosticoCommand, OrdemDeProducaoDto>();

        var resultado = await comportamento.Handle(Comando, async () =>
        {
            await _transacao.ConfirmarAsync(CancellationToken.None);
            return Resposta;
        }, CancellationToken.None);

        resultado.Should().BeSameAs(Resposta);
        _transacao.Iniciadas.Should().Be(1);
        _transacao.Confirmadas.Should().Be(1);
        _transacao.Abortadas.Should().Be(0);
    }

    [Fact]
    public async Task CommandHttp_QueTerminaSemSaveChanges_DeveAbortarATransacaoEDescartarMetricas()
    {
        using var captura = new CapturaDeMetricas();
        var comportamento = Criar<ConcluirDiagnosticoCommand, OrdemDeProducaoDto>();

        await comportamento.Handle(Comando, () =>
        {
            _metricas.OrdemEnfileirada();
            return Task.FromResult(Resposta);
        }, CancellationToken.None);

        _transacao.Abortadas.Should().Be(1);
        _metricas.Publicar();
        captura.Total("ordens_enfileiradas_total").Should().Be(0);
    }

    [Fact]
    public async Task ConflitoTransitorio_DeveReexecutarOHandlerInteiroEmNovaTransacao()
    {
        var comportamento = Criar<ConcluirDiagnosticoCommand, OrdemDeProducaoDto>();
        var execucoes = 0;

        var resultado = await comportamento.Handle(Comando, async () =>
        {
            execucoes++;
            if (execucoes < 3)
                throw ConflitoDeTransacao();

            await _transacao.ConfirmarAsync(CancellationToken.None);
            return Resposta;
        }, CancellationToken.None);

        resultado.Should().BeSameAs(Resposta);
        execucoes.Should().Be(3);
        _transacao.Iniciadas.Should().Be(3);
        _transacao.Abortadas.Should().Be(2);
        _transacao.Confirmadas.Should().Be(1);
    }

    [Fact]
    public async Task ConflitoTransitorio_NaoDeveContarMetricasDasTentativasAbortadas()
    {
        using var captura = new CapturaDeMetricas();
        var comportamento = Criar<ConcluirDiagnosticoCommand, OrdemDeProducaoDto>();
        var execucoes = 0;

        await comportamento.Handle(Comando, async () =>
        {
            execucoes++;
            _metricas.OrdemGravada(OficinaMecanica.Execucao.Domain.Enums.StatusProducao.DiagnosticoConcluido);
            if (execucoes == 1)
                throw ConflitoDeTransacao();

            await _transacao.ConfirmarAsync(CancellationToken.None);
            _metricas.Publicar();
            return Resposta;
        }, CancellationToken.None);

        captura.Total("diagnosticos_concluidos_total").Should().Be(1);
    }

    [Fact]
    public async Task ConflitoQueNaoSome_DeveEsgotarAsTentativasELancarConcorrenciaException()
    {
        var comportamento = Criar<ConcluirDiagnosticoCommand, OrdemDeProducaoDto>(maximo: 3);
        var execucoes = 0;

        var acao = () => comportamento.Handle(Comando, () =>
        {
            execucoes++;
            throw ConflitoDeTransacao();
        }, CancellationToken.None);

        await acao.Should().ThrowAsync<ConcorrenciaException>().WithMessage("*conflito de concorrência*");
        execucoes.Should().Be(3);
        _transacao.Iniciadas.Should().Be(3);
        _transacao.Aberta.Should().BeFalse();
    }

    [Fact]
    public async Task ExcecaoDeNegocio_DevePropagarAbortandoSemRetentar()
    {
        var comportamento = Criar<ConcluirDiagnosticoCommand, OrdemDeProducaoDto>();
        var execucoes = 0;

        var acao = () => comportamento.Handle(Comando, () =>
        {
            execucoes++;
            throw new DomainException("regra violada");
        }, CancellationToken.None);

        await acao.Should().ThrowAsync<DomainException>();
        execucoes.Should().Be(1);
        _transacao.Abortadas.Should().Be(1);
    }

    [Fact]
    public async Task ErroDoMongoSemRotuloTransitorio_NaoDeveSerRetentado()
    {
        var comportamento = Criar<ConcluirDiagnosticoCommand, OrdemDeProducaoDto>();
        var execucoes = 0;

        var acao = () => comportamento.Handle(Comando, () =>
        {
            execucoes++;
            throw new MongoException("falha permanente");
        }, CancellationToken.None);

        await acao.Should().ThrowAsync<MongoException>();
        execucoes.Should().Be(1);
    }

    [Fact]
    public async Task CancelamentoDuranteOBackoff_DeveInterromperSemReexecutar()
    {
        var comportamento = Criar<ConcluirDiagnosticoCommand, OrdemDeProducaoDto>(maximo: 5);
        using var cancelamento = new CancellationTokenSource();
        var execucoes = 0;

        var acao = () => comportamento.Handle(Comando, () =>
        {
            execucoes++;
            cancelamento.Cancel();
            throw ConflitoDeTransacao();
        }, cancelamento.Token);

        await acao.Should().ThrowAsync<OperationCanceledException>();
        execucoes.Should().Be(1);
        _transacao.Aberta.Should().BeFalse();
    }
}
