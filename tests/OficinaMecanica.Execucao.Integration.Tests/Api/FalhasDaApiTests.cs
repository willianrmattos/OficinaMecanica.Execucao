using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OficinaMecanica.Execucao.API.Middleware;
using OficinaMecanica.Execucao.Domain.Entities;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Domain.Exceptions;
using OficinaMecanica.Execucao.Domain.Interfaces;
using OficinaMecanica.Execucao.Integration.Tests.Support;

namespace OficinaMecanica.Execucao.Integration.Tests.Api;

public class FalhasDaApiTests : IDisposable
{
    // Repositorio que falha em toda leitura: representa conflito de concorrencia esgotado ou banco com problema.
    private sealed class RepositorioQueLanca(Exception excecao) : IOrdemDeProducaoRepository
    {
        public Task<OrdemDeProducao?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default) => throw excecao;
        public Task<bool> AdicionarAsync(OrdemDeProducao ordem, CancellationToken cancellationToken = default) => throw excecao;
        public Task AtualizarAsync(OrdemDeProducao ordem, CancellationToken cancellationToken = default) => throw excecao;
        public Task<OrdemDeProducao?> ObterProximaDaFilaAsync(StatusProducao status, CancellationToken cancellationToken = default) => throw excecao;
        public Task<IReadOnlyList<OrdemDeProducao>> ListarAsync(StatusProducao? status, string? mecanicoId, int pagina, int tamanho, CancellationToken cancellationToken = default) => throw excecao;
        public Task<int> ContarAsync(StatusProducao? status, string? mecanicoId, CancellationToken cancellationToken = default) => throw excecao;
        public Task<IReadOnlyDictionary<StatusProducao, int>> ContarPorStatusAsync(CancellationToken cancellationToken = default) => throw excecao;
    }

    private readonly List<ApiSemInfraFactory> _fabricas = new();

    private HttpClient ClienteQueFalhaCom(Exception excecao)
    {
        var fabrica = new ApiSemInfraFactory(services =>
        {
            services.Decorar<IOrdemDeProducaoRepository>((_, _) => new RepositorioQueLanca(excecao));
            // Sem o aviso de erro nao tratado no console dos testes.
            services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Critical));
        });
        _fabricas.Add(fabrica);
        return fabrica.CriarClienteAutenticado();
    }

    [Fact]
    public async Task ConcorrenciaEsgotada_DeveResponder409ComMensagemParaRepetir()
    {
        var cliente = ClienteQueFalhaCom(new ConcorrenciaException("A ordem de produção foi alterada por outra operação."));

        var resposta = await cliente.PostAsync("/api/diagnosticos/proximo", null);

        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await resposta.Content.ReadFromJsonAsync<ErrorResponse>())!.Tipo.Should().Be("Conflito");
    }

    [Fact]
    public async Task FalhaInesperada_DeveResponder500SemVazarDetalhesDoErro()
    {
        var cliente = ClienteQueFalhaCom(new InvalidOperationException("detalhe interno: connection string mongodb://x:y@z"));

        var resposta = await cliente.GetAsync("/api/fila/resumo");

        resposta.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var corpo = await resposta.Content.ReadAsStringAsync();
        corpo.Should().Contain("Ocorreu um erro interno no servidor.");
        corpo.Should().NotContain("mongodb").And.NotContain("detalhe interno");
    }

    public void Dispose()
    {
        foreach (var fabrica in _fabricas)
            fabrica.Dispose();

        GC.SuppressFinalize(this);
    }
}
