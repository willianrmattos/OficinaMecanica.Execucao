using Bogus;
using OficinaMecanica.Execucao.Domain.Entities;

namespace OficinaMecanica.Execucao.Tests.Common.Builders;

public class CancelamentoPrevioBuilder
{
    private static readonly Faker Faker = new("pt_BR");

    private Guid _ordemDeServicoId = Guid.NewGuid();
    private string _motivo = Faker.Lorem.Sentence();
    private DateTimeOffset _registradoEm = Faker.Date.PastOffset();

    public CancelamentoPrevioBuilder DaOrdemDeServico(Guid ordemDeServicoId) { _ordemDeServicoId = ordemDeServicoId; return this; }
    public CancelamentoPrevioBuilder ComMotivo(string motivo) { _motivo = motivo; return this; }
    public CancelamentoPrevioBuilder RegistradoEm(DateTimeOffset registradoEm) { _registradoEm = registradoEm; return this; }

    public CancelamentoPrevio Build() => new(_ordemDeServicoId, _motivo, _registradoEm);
}
