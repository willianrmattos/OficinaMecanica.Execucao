using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using OficinaMecanica.Execucao.Application.DTOs;
using OficinaMecanica.Execucao.Domain.Entities;
using OficinaMecanica.Execucao.Domain.Interfaces;

namespace OficinaMecanica.Execucao.Integration.Tests.Support;

// Um "mini ecossistema" por teste: a Execucao inteira (API + consumers + Mongo + RabbitMQ reais) e o OS Service
// simulado no mesmo virtual host, com banco exclusivo.
public sealed class AmbienteDeInfra : IAsyncDisposable
{
    public ApiComInfraFactory Api { get; }

    public OsServiceSimulado Os { get; }

    private AmbienteDeInfra(ApiComInfraFactory api, OsServiceSimulado os)
    {
        Api = api;
        Os = os;
    }

    public static async Task<AmbienteDeInfra> CriarAsync(
        IEndpointsDeInfra infra,
        Action<IServiceCollection>? ajustes = null,
        IReadOnlyDictionary<string, string>? configuracao = null)
    {
        var virtualHost = await infra.CriarVirtualHostAsync();
        var os = await OsServiceSimulado.IniciarAsync(infra, virtualHost);
        var api = new ApiComInfraFactory(infra, virtualHost, ajustes, configuracao);

        // Forca o start do host (indices, bus e consumers) antes do primeiro envio.
        _ = api.Server;

        return new AmbienteDeInfra(api, os);
    }

    public HttpClient Cliente(string? mecanico = "mecanico-01") => Api.CriarClienteAutenticado(mecanico);

    public IMongoCollection<BsonDocument> Documentos => Api.Ordens;

    public async Task SemearAsync(params OrdemDeProducao[] ordens)
    {
        using var escopo = Api.Services.CreateScope();
        var repositorio = escopo.ServiceProvider.GetRequiredService<IOrdemDeProducaoRepository>();

        foreach (var ordem in ordens)
            (await repositorio.AdicionarAsync(ordem)).Should().BeTrue();
    }

    public async Task<BsonDocument?> DocumentoDaOrdemAsync(Guid id) =>
        await Documentos.Find(new BsonDocument("_id", new BsonBinaryData(id, GuidRepresentation.Standard))).FirstOrDefaultAsync();

    public async Task<OrdemDeProducaoDto?> ConsultarAsync(Guid id)
    {
        var resposta = await Cliente().GetAsync($"/api/ordens-de-producao/{id}");

        return resposta.StatusCode == System.Net.HttpStatusCode.NotFound
            ? null
            : await resposta.Content.ReadFromJsonAsync<OrdemDeProducaoDto>();
    }

    public async Task<long> ContarNoBancoAsync(string colecao) =>
        await Api.Banco.GetCollection<BsonDocument>(colecao).CountDocumentsAsync(new BsonDocument());

    public async ValueTask DisposeAsync()
    {
        await Os.DisposeAsync();
        Api.Dispose();
    }
}
