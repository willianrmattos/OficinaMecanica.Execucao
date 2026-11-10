using Bogus;
using OficinaMecanica.Contracts.Commands;
using OficinaMecanica.Contracts.Common;

namespace OficinaMecanica.Execucao.Tests.Common.Builders;

public class SolicitarDiagnosticoBuilder
{
    private static readonly Faker Faker = new("pt_BR");

    private Guid _ordemDeServicoId = Guid.NewGuid();
    private string _numero = $"OS-{Faker.Date.Past():yyyyMMdd}-{Faker.Random.AlphaNumeric(6).ToUpperInvariant()}";
    private string _placa = Faker.Random.Replace("???#?##").ToUpperInvariant();
    private string _marca = Faker.Vehicle.Manufacturer();
    private string _modelo = Faker.Vehicle.Model();
    private string? _observacoes = Faker.Lorem.Sentence();
    private List<ItemDeServicoMensagem> _servicos = [new ItemDeServicoMensagemBuilder().Build()];
    private List<ItemDePecaMensagem> _pecas = [new ItemDePecaMensagemBuilder().Build()];

    public SolicitarDiagnosticoBuilder ComOrdemDeServicoId(Guid ordemDeServicoId) { _ordemDeServicoId = ordemDeServicoId; return this; }
    public SolicitarDiagnosticoBuilder ComServicos(params ItemDeServicoMensagem[] servicos) { _servicos = servicos.ToList(); return this; }
    public SolicitarDiagnosticoBuilder ComPecas(params ItemDePecaMensagem[] pecas) { _pecas = pecas.ToList(); return this; }
    public SolicitarDiagnosticoBuilder SemObservacoes() { _observacoes = null; return this; }

    public SolicitarDiagnostico Build() =>
        new(Guid.NewGuid(), _ordemDeServicoId, Faker.Date.RecentOffset(), _numero, _placa, _marca, _modelo, _observacoes, _servicos, _pecas);
}
