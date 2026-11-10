using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using OficinaMecanica.Contracts.Commands;
using OficinaMecanica.Execucao.Domain.Interfaces;
using OficinaMecanica.Execucao.Integration.Tests.Support;
using Xunit.Abstractions;

namespace OficinaMecanica.Execucao.Integration.Tests.Infra;

// A saga manda SolicitarDiagnostico e CancelarProducao ao mesmo endpoint, entao as duas mensagens podem rodar
// em paralelo. Ordem e tombstone moram no mesmo _id: a escrita que perde colide, o retry reexecuta o handler e
// a pre-checagem resolve. O invariante e que nunca sobra ordem na Fila para uma producao cancelada.
[Trait("Category", "Infra")]
[Collection(Colecoes.Infra)]
public class CorridaEnfileirarCancelarTests(InfraFixture infra, ITestOutputHelper saida)
{
    private async Task<(IReadOnlyList<Guid> Ids, AmbienteDeInfra Ambiente, RegistroDeInsercoes Registro, PontoDeEncontro? Encontro)>
        ExecutarRodadasAsync(int rodadas, bool forcarEncontro)
    {
        var registro = new RegistroDeInsercoes();
        var encontro = forcarEncontro ? new PontoDeEncontro(TimeSpan.FromSeconds(5)) : null;

        var ambiente = await AmbienteDeInfra.CriarAsync(infra, services =>
        {
            services.AddSingleton(registro);

            if (encontro is not null)
            {
                services.AddSingleton(encontro);
                services.Decorar<ICancelamentoPrevioRepository>((interno, sp) =>
                    new TombstonesComEncontro(interno, sp.GetRequiredService<PontoDeEncontro>()));
            }

            services.Decorar<ICancelamentoPrevioRepository>((interno, sp) =>
                new TombstonesQueRegistramInsercoes(interno, sp.GetRequiredService<RegistroDeInsercoes>()));
            services.Decorar<IOrdemDeProducaoRepository>((interno, sp) =>
                new OrdensQueRegistramInsercoes(interno, sp.GetRequiredService<RegistroDeInsercoes>()));
        });

        var ids = Enumerable.Range(0, rodadas).Select(_ => Guid.NewGuid()).ToList();
        foreach (var id in ids)
        {
            await Task.WhenAll(
                ambiente.Os.EnviarAsync(new SolicitarDiagnosticoBuilder().ComOrdemDeServicoId(id).Build()),
                ambiente.Os.EnviarAsync(new CancelarProducaoBuilder().ComOrdemDeServicoId(id).Build()));
        }

        return (ids, ambiente, registro, encontro);
    }

    private static async Task<bool> EstadoFinalValidoAsync(AmbienteDeInfra ambiente, Guid id)
    {
        var documento = await ambiente.DocumentoDaOrdemAsync(id);
        if (documento is null)
            return false;

        return documento["_t"].AsString == "cancelamentoPrevio"
               || (documento["_t"].AsString == "ordem" && documento["status"].AsString == "Cancelada");
    }

    private async Task VerificarInvariantesAsync(AmbienteDeInfra ambiente, IReadOnlyList<Guid> ids)
    {
        // Se sobrasse uma ordem orfa na Fila, o estado final nunca seria alcancado e a espera estouraria.
        (await Esperar.AteAsync(async () =>
        {
            foreach (var id in ids)
                if (!await EstadoFinalValidoAsync(ambiente, id))
                    return false;
            return true;
        }, limiteEmMs: 60_000)).Should().BeTrue("toda producao cancelada deve terminar cancelada ou como tombstone");

        // Estabilidade: espero as retentativas pendentes drenarem e confirmo que nada mudou.
        await Task.Delay(1_500);
        (await ambiente.Documentos.CountDocumentsAsync(new BsonDocument())).Should().Be(ids.Count, "um unico documento por ordem de servico");
        (await ambiente.Documentos.CountDocumentsAsync(
            new BsonDocument { { "_t", "ordem" }, { "status", new BsonDocument("$ne", "Cancelada") } })).Should().Be(0);
        (await ambiente.Documentos.CountDocumentsAsync(new BsonDocument("status", "Fila"))).Should().Be(0);
        ambiente.Os.Gravador.Falhas.Should().Be(0, "a colisao e resolvida por retry, nunca por fila de erro");
    }

    [Fact(Timeout = 300_000)]
    public async Task EnfileirarECancelarEmParaleloForcado_VariasRodadas_NuncaDeixaOrdemOrfaNaFila()
    {
        var (ids, ambiente, registro, encontro) = await ExecutarRodadasAsync(rodadas: 12, forcarEncontro: true);
        await using var _ = ambiente;

        await VerificarInvariantesAsync(ambiente, ids);

        // O encontro garante que os dois handlers passaram pelas pre-checagens antes de qualquer escrita: em toda
        // rodada exatamente uma das duas insercoes colide na chave unica.
        encontro!.EncontrosCompletos.Should().Be(ids.Count);
        registro.Colisoes.Should().Be(ids.Count);
        foreach (var id in ids)
            registro.De(id).Count(d => d.StartsWith("colisao:")).Should().Be(1);

        var vencedores = ids.Select(id => registro.De(id).Single(d => !d.StartsWith("colisao:"))).GroupBy(d => d)
            .ToDictionary(g => g.Key, g => g.Count());
        saida.WriteLine("Vencedor da insercao por rodada: " + string.Join(", ", vencedores.Select(v => $"{v.Key}={v.Value}")));
    }

    [Fact(Timeout = 300_000)]
    public async Task EnfileirarECancelarEmParaleloSemEncontro_VariasRodadas_NuncaDeixaOrdemOrfaNaFila()
    {
        var (ids, ambiente, _, _) = await ExecutarRodadasAsync(rodadas: 30, forcarEncontro: false);
        await using var _ = ambiente;

        await VerificarInvariantesAsync(ambiente, ids);
    }

    [Fact(Timeout = 300_000)]
    public async Task CancelarAntesDeEnfileirar_DeveRegistrarTombstoneEIgnorarOEnfileiramentoAtrasado()
    {
        await using var ambiente = await AmbienteDeInfra.CriarAsync(infra);
        var id = Guid.NewGuid();

        await ambiente.Os.EnviarAsync(new CancelarProducaoBuilder().ComOrdemDeServicoId(id).Build());
        (await Esperar.AteAsync(async () => (await ambiente.DocumentoDaOrdemAsync(id)) is not null)).Should().BeTrue();
        await ambiente.Os.EnviarAsync(new SolicitarDiagnosticoBuilder().ComOrdemDeServicoId(id).Build());
        await Task.Delay(2_000);

        var documento = await ambiente.DocumentoDaOrdemAsync(id);
        documento!["_t"].AsString.Should().Be("cancelamentoPrevio");
        (await ambiente.Documentos.CountDocumentsAsync(new BsonDocument("_t", "ordem"))).Should().Be(0);
        (await ambiente.Cliente().GetAsync($"/api/ordens-de-producao/{id}")).StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
        (await ambiente.Cliente().GetAsync("/api/fila/resumo")).EnsureSuccessStatusCode();
        ambiente.Os.Gravador.Falhas.Should().Be(0);
    }
}
