using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Bson;
using MongoDB.Driver;
using OficinaMecanica.Contracts.Events;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Domain.Exceptions;
using OficinaMecanica.Execucao.Domain.Interfaces;
using OficinaMecanica.Execucao.Infrastructure.Persistence;
using OficinaMecanica.Execucao.Integration.Tests.Support;

namespace OficinaMecanica.Execucao.Integration.Tests.Infra;

// Um WriteConflict dentro da transacao chega como TransientTransactionError: o TransacaoBehavior reexecuta o
// handler inteiro. O conflito e provocado de verdade, com uma transacao do proprio teste segurando uma
// escrita nao commitada no documento da ordem.
[Trait("Category", "Infra")]
[Collection(Colecoes.Infra)]
public class RetryDeTransacaoTests(InfraFixture infra)
{
    private const string Mecanico = "mecanico-retry";

    private sealed class TransacaoQueConta(ITransacaoMongo interno, ContadorDeChamadas contador) : ITransacaoMongo
    {
        public IClientSessionHandle? Current => interno.Current;
        public bool Ambiente => interno.Ambiente;

        public Task IniciarAsync(CancellationToken cancellationToken)
        {
            contador.Registrar();
            return interno.IniciarAsync(cancellationToken);
        }

        public Task ConfirmarAsync(CancellationToken cancellationToken) => interno.ConfirmarAsync(cancellationToken);

        public Task AbortarAsync() => interno.AbortarAsync();
    }

    private Task<AmbienteDeInfra> CriarAsync(ContadorDeChamadas contador, int maximoDeTentativas) =>
        AmbienteDeInfra.CriarAsync(infra, services =>
        {
            services.AddSingleton(contador);
            services.RemoveAll<TransacaoOptions>();
            services.AddSingleton(new TransacaoOptions { MaximoDeTentativas = maximoDeTentativas, AtrasoBaseEmMs = 20 });
            services.Decorar<ITransacaoMongo>((interno, sp) => new TransacaoQueConta(interno, sp.GetRequiredService<ContadorDeChamadas>()));
        });

    // Escrita nao commitada no documento: qualquer transacao que tente escrever nele leva WriteConflict.
    private static async Task<IClientSessionHandle> SegurarEscritaAsync(AmbienteDeInfra ambiente, Guid ordemId)
    {
        var sessao = await ambiente.Api.Banco.Client.StartSessionAsync();
        sessao.StartTransaction();
        await ambiente.Documentos.UpdateOneAsync(
            sessao,
            new BsonDocument("_id", new BsonBinaryData(ordemId, GuidRepresentation.Standard)),
            new BsonDocument("$set", new BsonDocument("bloqueio", true)));
        return sessao;
    }

    [Fact(Timeout = 300_000)]
    public async Task ConflitoDeEscrita_DeveReexecutarOHandlerEConcluirQuandoOConflitoSome()
    {
        var contador = new ContadorDeChamadas();
        await using var ambiente = await CriarAsync(contador, maximoDeTentativas: 40);
        var ordem = new OrdemDeProducaoBuilder().DoMecanico(Mecanico).EmDiagnostico();
        await ambiente.SemearAsync(ordem);
        using var bloqueio = await SegurarEscritaAsync(ambiente, ordem.Id);

        var requisicao = ambiente.Cliente(Mecanico).PostAsJsonAsync(
            $"/api/ordens-de-producao/{ordem.Id}/diagnostico/concluir", new { laudo = "Folga na suspensao." });
        (await Esperar.AteAsync(() => contador.Total >= 2, 10_000)).Should().BeTrue("o handler deve ser reexecutado em nova transacao");
        await bloqueio.AbortTransactionAsync();

        var resposta = await requisicao;

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        contador.Total.Should().BeGreaterThanOrEqualTo(2);
        var documento = await ambiente.DocumentoDaOrdemAsync(ordem.Id);
        documento!["status"].AsString.Should().Be("DiagnosticoConcluido");
        documento.Contains("bloqueio").Should().BeFalse();
        (await Esperar.AteAsync(() => ambiente.Os.Gravador.De<DiagnosticoConcluido>().Count == 1)).Should().BeTrue();
        await Task.Delay(500);
        ambiente.Os.Gravador.De<DiagnosticoConcluido>().Should().ContainSingle();
    }

    [Fact(Timeout = 300_000)]
    public async Task ConflitoQueNaoSome_DeveEsgotarAsTentativasERetornar409SemAlterarAOrdem()
    {
        var contador = new ContadorDeChamadas();
        await using var ambiente = await CriarAsync(contador, maximoDeTentativas: 3);
        var ordem = new OrdemDeProducaoBuilder().DoMecanico(Mecanico).EmDiagnostico();
        await ambiente.SemearAsync(ordem);
        using var bloqueio = await SegurarEscritaAsync(ambiente, ordem.Id);

        var resposta = await ambiente.Cliente(Mecanico).PostAsJsonAsync(
            $"/api/ordens-de-producao/{ordem.Id}/diagnostico/concluir", new { laudo = "Folga na suspensao." });
        await bloqueio.AbortTransactionAsync();

        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        contador.Total.Should().Be(3);
        var documento = await ambiente.DocumentoDaOrdemAsync(ordem.Id);
        documento!["status"].AsString.Should().Be("EmDiagnostico");
        (await ambiente.ContarNoBancoAsync("outbox.messages")).Should().Be(0);
    }

    [Fact(Timeout = 300_000)]
    public async Task ReivindicacaoComSessaoAindaUtilizavelDepoisDeConcorrenciaException_DeveVerAProximaOrdem()
    {
        // O claim le a fila fora da transacao e escreve dentro dela: quando a Version ficou velha (outro mecanico
        // levou a ordem entre a leitura e a escrita) o filtro nao casa e sobe ConcorrenciaException sem abortar
        // a transacao; a releitura enxerga a proxima ordem e o commit funciona.
        await using var ambiente = await CriarAsync(new ContadorDeChamadas(), maximoDeTentativas: 3);
        var primeira = new OrdemDeProducaoBuilder().EnfileiradaEm(DateTimeOffset.UtcNow.AddHours(-2)).NaFila();
        var segunda = new OrdemDeProducaoBuilder().EnfileiradaEm(DateTimeOffset.UtcNow.AddHours(-1)).NaFila();
        await ambiente.SemearAsync(primeira, segunda);

        using var escopo = ambiente.Api.Services.CreateScope();
        var transacao = escopo.ServiceProvider.GetRequiredService<ITransacaoMongo>();
        var repositorio = escopo.ServiceProvider.GetRequiredService<IOrdemDeProducaoRepository>();
        var lidaPeloMecanicoA = await repositorio.ObterProximaDaFilaAsync(StatusProducao.Fila);
        await using (var outroEscopo = ambiente.Api.Services.CreateAsyncScope())
        {
            var outro = outroEscopo.ServiceProvider.GetRequiredService<IOrdemDeProducaoRepository>();
            var lidaPeloMecanicoB = await outro.ObterProximaDaFilaAsync(StatusProducao.Fila);
            lidaPeloMecanicoB!.IniciarDiagnostico("mecanico-b", DateTimeOffset.UtcNow);
            await outro.AtualizarAsync(lidaPeloMecanicoB);
        }

        await transacao.IniciarAsync(CancellationToken.None);
        lidaPeloMecanicoA!.IniciarDiagnostico("mecanico-a", DateTimeOffset.UtcNow);
        var tentativa = () => repositorio.AtualizarAsync(lidaPeloMecanicoA);

        await tentativa.Should().ThrowAsync<ConcorrenciaException>();
        lidaPeloMecanicoA.Version.Should().Be(0, "a ordem em memoria so avanca depois de a escrita ser aceita");
        var proxima = await repositorio.ObterProximaDaFilaAsync(StatusProducao.Fila);
        proxima!.Id.Should().Be(segunda.Id, "a releitura enxerga o estado ja commitado");
        proxima.IniciarDiagnostico("mecanico-a", DateTimeOffset.UtcNow);
        await repositorio.AtualizarAsync(proxima);
        await transacao.ConfirmarAsync(CancellationToken.None);

        (await ambiente.DocumentoDaOrdemAsync(segunda.Id))!["mecanicoId"].AsString.Should().Be("mecanico-a");
        (await ambiente.DocumentoDaOrdemAsync(primeira.Id))!["mecanicoId"].AsString.Should().Be("mecanico-b");
    }
}
