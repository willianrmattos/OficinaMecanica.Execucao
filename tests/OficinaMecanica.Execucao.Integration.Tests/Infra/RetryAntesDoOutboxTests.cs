using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using OficinaMecanica.Contracts.Events;
using OficinaMecanica.Execucao.Application.Commands.EnfileirarOrdemDeProducao;
using OficinaMecanica.Execucao.Application.Interfaces;
using OficinaMecanica.Execucao.Integration.Tests.Support;

namespace OficinaMecanica.Execucao.Integration.Tests.Infra;

// Protege a ordem dos filtros do endpoint: UseMessageRetry ANTES de UseMongoDbOutbox. O que a ordem muda e se o
// proprio filtro do outbox (registro no inbox, abertura e commit da transacao) esta dentro ou fora do retry: com o
// retry por fora, uma falha transitoria do outbox e retentada; com o outbox por fora, ela escapa do retry e a
// mensagem vai para a fila de erro. O teste injeta essa falha de verdade, no cliente Mongo que o outbox usa.
[Trait("Category", "Infra")]
[Collection(Colecoes.Infra)]
public class RetryAntesDoOutboxTests(InfraFixture infra)
{
    // Publica um evento de integracao (que vai para o outbox do consumer) e so depois falha nas primeiras tentativas.
    private sealed class PublicaEFalhaBehavior<TRequest, TResponse>(
        IIntegrationEventPublisher publicador, TentativasDoHandler tentativas)
        : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            if (request is not EnfileirarOrdemDeProducaoCommand comando)
                return await next();

            var tentativa = tentativas.Registrar();
            await publicador.PublishAsync(
                new DiagnosticoIniciado(Guid.NewGuid(), comando.OrdemDeServicoId, DateTimeOffset.UtcNow, $"tentativa-{tentativa}"),
                cancellationToken);

            if (tentativa <= tentativas.Falhas)
                throw new InvalidOperationException($"Falha transitoria injetada na tentativa {tentativa}.");

            return await next();
        }
    }

    private sealed class TentativasDoHandler(int falhas)
    {
        private int _tentativas;

        public int Falhas => falhas;

        public int Total => Volatile.Read(ref _tentativas);

        public int Registrar() => Interlocked.Increment(ref _tentativas);
    }

    // Cliente Mongo que falha as primeiras aberturas de sessao pedidas PELO FILTRO DO OUTBOX DO CONSUMER (identificado
    // pela pilha de chamadas): o servico de entrega do outbox tambem abre sessoes em segundo plano e absorveria a
    // falha, e os repositorios usam a sessao que o filtro abriu. Assim a falha cai no proprio filtro, nao no handler.
    public class ClienteQueFalhaAoAbrirSessao : DispatchProxy
    {
        private int _restantes;

        public IMongoClient Alvo { get; set; } = null!;

        public int Injetadas { get; private set; }

        public void Falhar(int vezes) => _restantes = vezes;

        protected override object? Invoke(MethodInfo? metodo, object?[]? argumentos)
        {
            if (metodo!.Name.StartsWith("StartSession", StringComparison.Ordinal)
                && new StackTrace().ToString().Contains("MongoDbOutboxContextFactory", StringComparison.Ordinal)
                && Interlocked.Decrement(ref _restantes) >= 0)
            {
                Injetadas++;
                throw new InvalidOperationException("Falha transitoria injetada ao abrir a sessao do outbox.");
            }

            try
            {
                return metodo.Invoke(Alvo, argumentos);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }
    }

    [Fact(Timeout = 300_000)]
    public async Task FalhaTransitoriaDoProprioFiltroDoOutbox_DeveSerRetentadaEnaoIrParaAFilaDeErro()
    {
        ClienteQueFalhaAoAbrirSessao? cliente = null;
        await using var ambiente = await AmbienteDeInfra.CriarAsync(infra, services =>
            services.Decorar<IMongoClient>((interno, _) =>
            {
                var proxy = DispatchProxy.Create<IMongoClient, ClienteQueFalhaAoAbrirSessao>();
                cliente = (ClienteQueFalhaAoAbrirSessao)(object)proxy;
                cliente.Alvo = interno;
                return proxy;
            }));
        _ = ambiente.Api.Services.GetRequiredService<IMongoClient>();
        cliente!.Falhar(2);
        var mensagem = new SolicitarDiagnosticoBuilder().Build();

        await ambiente.Os.EnviarAsync(mensagem);

        (await Esperar.AteAsync(async () => await ambiente.DocumentoDaOrdemAsync(mensagem.OrdemDeServicoId) is not null))
            .Should().BeTrue("o retry por fora do outbox reexecuta a mensagem inteira, inclusive o proprio filtro");
        await Task.Delay(1_500);
        cliente.Injetadas.Should().Be(2);
        ambiente.Os.Gravador.Falhas.Should().Be(0, "a falha do outbox foi absorvida pelo retry, nao foi para _error");
        (await ambiente.Documentos.CountDocumentsAsync(new BsonDocument("_t", "ordem"))).Should().Be(1);
    }

    // A publicacao de uma tentativa que falhou nao pode sair do outbox: so a da tentativa que commitou.
    [Fact(Timeout = 300_000)]
    public async Task ConsumerQuePublicaEFalhaDuasVezes_DeveEntregarExatamenteUmEventoESemOrfaosNoOutbox()
    {
        var tentativas = new TentativasDoHandler(falhas: 2);
        await using var ambiente = await AmbienteDeInfra.CriarAsync(infra, services =>
        {
            services.AddSingleton(tentativas);
            services.Insert(0, ServiceDescriptor.Transient(typeof(IPipelineBehavior<,>), typeof(PublicaEFalhaBehavior<,>)));
        });
        var mensagem = new SolicitarDiagnosticoBuilder().Build();

        await ambiente.Os.EnviarAsync(mensagem);

        (await Esperar.AteAsync(() => ambiente.Os.Gravador.De<DiagnosticoIniciado>().Count >= 1)).Should().BeTrue();
        (await Esperar.AteAsync(() => tentativas.Total >= 3)).Should().BeTrue();
        await Task.Delay(2_500);

        tentativas.Total.Should().Be(3, "duas falhas retentadas e a tentativa que concluiu");
        var entregues = ambiente.Os.Gravador.De<DiagnosticoIniciado>();
        entregues.Should().ContainSingle("so a publicacao da tentativa que commitou pode sair do outbox");
        (await ambiente.ContarNoBancoAsync("outbox.messages")).Should().Be(0, "nenhuma mensagem orfa das tentativas que falharam");
        (await ambiente.Documentos.CountDocumentsAsync(new BsonDocument("_t", "ordem"))).Should().Be(1);
        ambiente.Os.Gravador.Falhas.Should().Be(0);
    }
}
