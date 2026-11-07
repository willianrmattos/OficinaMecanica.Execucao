using Bogus;
using OficinaMecanica.Contracts.Commands;

namespace OficinaMecanica.Execucao.Tests.Common.Builders;

public class CancelarProducaoBuilder
{
    private static readonly Faker Faker = new("pt_BR");

    private Guid _ordemDeServicoId = Guid.NewGuid();
    private string _motivo = Faker.Lorem.Sentence();

    public CancelarProducaoBuilder ComOrdemDeServicoId(Guid ordemDeServicoId) { _ordemDeServicoId = ordemDeServicoId; return this; }
    public CancelarProducaoBuilder ComMotivo(string motivo) { _motivo = motivo; return this; }

    public CancelarProducao Build() =>
        new(Guid.NewGuid(), _ordemDeServicoId, Faker.Date.RecentOffset(), _motivo);
}
