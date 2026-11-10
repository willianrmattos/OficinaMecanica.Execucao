using System.Net;
using System.Net.Http.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using OficinaMecanica.Contracts.Events;
using OficinaMecanica.Execucao.Application.DTOs;
using OficinaMecanica.Execucao.Domain.Entities;
using OficinaMecanica.Execucao.Integration.Tests.Support;

namespace OficinaMecanica.Execucao.Integration.Tests.Infra;

// N mecanicos disputando M ordens pela API: a mesma ordem nunca vai para dois mecanicos, a fila e FIFO e cada
// claim de diagnostico publica exatamente um DiagnosticoIniciado.
[Trait("Category", "Infra")]
[Collection(Colecoes.Infra)]
public class ClaimConcorrenteTests(InfraFixture infra)
{
    private static readonly DateTimeOffset Base = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    private sealed record Resultado(IReadOnlyDictionary<string, List<Guid>> PorMecanico, int Conflitos);

    // Teto da disputa inteira: um claim sem controle de concorrencia faria os mecanicos reivindicarem sem parar, e o
    // teste tem que ficar vermelho em vez de travar a execucao.
    private static readonly TimeSpan LimiteDaDisputa = TimeSpan.FromMinutes(2);

    // 409 e a resposta honesta para "disputa nao resolvida nas tentativas": o cliente repete a chamada.
    private static async Task<Resultado> DisputarAsync(AmbienteDeInfra ambiente, string rota, int mecanicos)
    {
        var porMecanico = Enumerable.Range(1, mecanicos).ToDictionary(i => $"mecanico-{i:00}", _ => new List<Guid>());
        var conflitos = 0;
        using var limite = new CancellationTokenSource(LimiteDaDisputa);

        await Task.WhenAll(porMecanico.Select(async entrada =>
        {
            var cliente = ambiente.Cliente(entrada.Key);

            while (true)
            {
                var resposta = await cliente.PostAsync(rota, null, limite.Token);

                if (resposta.StatusCode == HttpStatusCode.NoContent)
                    return;

                if (resposta.StatusCode == HttpStatusCode.Conflict)
                {
                    Interlocked.Increment(ref conflitos);
                    continue;
                }

                resposta.StatusCode.Should().Be(HttpStatusCode.OK);
                var ordem = await resposta.Content.ReadFromJsonAsync<OrdemDeProducaoDto>(limite.Token);
                entrada.Value.Add(ordem!.Id);
            }
        }));

        return new Resultado(porMecanico, conflitos);
    }

    private static void VerificarSemRepeticaoEEmOrdem(Resultado resultado, IReadOnlyList<Guid> filaEmOrdem)
    {
        var todas = resultado.PorMecanico.Values.SelectMany(x => x).ToList();

        todas.Should().OnlyHaveUniqueItems("a mesma ordem nunca pode ir para dois mecanicos");
        todas.Should().BeEquivalentTo(filaEmOrdem, "toda a fila deve ser consumida");

        // Cada mecanico reivindica em sequencia: o que ele recebe depois esta mais adiante na fila (FIFO).
        foreach (var (_, ids) in resultado.PorMecanico)
        {
            var posicoes = ids.Select(id => filaEmOrdem.ToList().IndexOf(id)).ToList();
            posicoes.Should().BeInAscendingOrder();
        }
    }

    [Fact(Timeout = 240_000)]
    public async Task DiagnosticoConcorrente_SeisMecanicosVinteEQuatroOrdens_NuncaRepeteEPublicaUmEventoPorClaim()
    {
        await using var ambiente = await AmbienteDeInfra.CriarAsync(infra);
        var fila = Enumerable.Range(0, 24)
            .Select(i => new OrdemDeProducaoBuilder().EnfileiradaEm(Base.AddSeconds(i)).NaFila())
            .ToList();
        await ambiente.SemearAsync(fila.ToArray());

        var resultado = await DisputarAsync(ambiente, "/api/diagnosticos/proximo", mecanicos: 6);

        VerificarSemRepeticaoEEmOrdem(resultado, fila.Select(o => o.Id).ToList());
        foreach (var (mecanico, ids) in resultado.PorMecanico)
            foreach (var id in ids)
            {
                var documento = await ambiente.DocumentoDaOrdemAsync(id);
                documento!["status"].AsString.Should().Be("EmDiagnostico");
                documento["mecanicoId"].AsString.Should().Be(mecanico, "o dono no banco e quem recebeu a resposta");
            }

        (await Esperar.AteAsync(() => ambiente.Os.Gravador.De<DiagnosticoIniciado>().Count == 24)).Should().BeTrue();
        await Task.Delay(1_000);
        var eventos = ambiente.Os.Gravador.De<DiagnosticoIniciado>();
        eventos.Should().HaveCount(24);
        eventos.Select(e => e.OrdemDeServicoId).Should().OnlyHaveUniqueItems();
        eventos.Select(e => e.MessageId).Should().OnlyHaveUniqueItems();
    }

    [Fact(Timeout = 240_000)]
    public async Task ReparoConcorrente_SeisMecanicos_NuncaRepeteEConsomePelaOrdemDeLiberacao()
    {
        await using var ambiente = await AmbienteDeInfra.CriarAsync(infra);
        var fila = Enumerable.Range(0, 18).Select(CriarLiberadaEmOrdemInversa).ToList();
        await ambiente.SemearAsync(fila.ToArray());
        var emOrdemDeLiberacao = fila.OrderBy(o => o.LiberadaEm).Select(o => o.Id).ToList();

        var resultado = await DisputarAsync(ambiente, "/api/reparos/proximo", mecanicos: 6);

        VerificarSemRepeticaoEEmOrdem(resultado, emOrdemDeLiberacao);
        foreach (var (mecanico, ids) in resultado.PorMecanico)
            foreach (var id in ids)
            {
                var documento = await ambiente.DocumentoDaOrdemAsync(id);
                documento!["status"].AsString.Should().Be("EmExecucao");
                documento["mecanicoId"].AsString.Should().Be(mecanico);
            }
    }

    [Fact(Timeout = 240_000)]
    public async Task ReivindicacaoSequencial_DeveSeguirExatamenteAOrdemDaFila()
    {
        await using var ambiente = await AmbienteDeInfra.CriarAsync(infra);
        var fila = Enumerable.Range(0, 5)
            .Select(i => new OrdemDeProducaoBuilder().EnfileiradaEm(Base.AddMinutes(-i)).NaFila())
            .ToList();
        await ambiente.SemearAsync(fila.ToArray());
        var cliente = ambiente.Cliente("mecanico-fifo");

        var recebidas = new List<Guid>();
        for (var i = 0; i < 5; i++)
            recebidas.Add((await (await cliente.PostAsync("/api/diagnosticos/proximo", null)).Content.ReadFromJsonAsync<OrdemDeProducaoDto>())!.Id);

        recebidas.Should().Equal(fila.OrderBy(o => o.EnfileiradaEm).Select(o => o.Id));
        (await cliente.PostAsync("/api/diagnosticos/proximo", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact(Timeout = 240_000)]
    public async Task ReivindicacaoDeDiagnostico_NaoDeveLevarOrdensDeOutroStatusNemTombstones()
    {
        await using var ambiente = await AmbienteDeInfra.CriarAsync(infra);
        var naFila = new OrdemDeProducaoBuilder().NaFila();
        await ambiente.SemearAsync(new OrdemDeProducaoBuilder().EmDiagnostico(), new OrdemDeProducaoBuilder().Finalizada(), naFila);
        await ambiente.Documentos.InsertOneAsync(
            new CancelamentoPrevioBuilder().Build().ToBsonDocument());

        var cliente = ambiente.Cliente("mecanico-x");
        var primeira = await (await cliente.PostAsync("/api/diagnosticos/proximo", null)).Content.ReadFromJsonAsync<OrdemDeProducaoDto>();

        primeira!.Id.Should().Be(naFila.Id);
        (await cliente.PostAsync("/api/diagnosticos/proximo", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // Enfileirada primeiro e liberada por ultimo (e vice-versa): so a ordenacao por LiberadaEm acerta a vez.
    private static OrdemDeProducao CriarLiberadaEmOrdemInversa(int i)
    {
        var ordem = new OrdemDeProducaoBuilder().EnfileiradaEm(Base.AddMinutes(i)).DiagnosticoConcluido();
        ordem.LiberarParaExecucao(ordem.Servicos, ordem.Pecas, Base.AddHours(1).AddMinutes(-i));
        return ordem;
    }
}
