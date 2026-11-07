using Bogus;
using OficinaMecanica.Contracts.Commands;
using OficinaMecanica.Contracts.Common;

namespace OficinaMecanica.Execucao.Tests.Common.Builders;

public class IniciarExecucaoBuilder
{
    private static readonly Faker Faker = new("pt_BR");

    private Guid _ordemDeServicoId = Guid.NewGuid();
    private string _numero = $"OS-{Faker.Date.Past():yyyyMMdd}-{Faker.Random.AlphaNumeric(6).ToUpperInvariant()}";
    private List<ItemDeServicoMensagem> _servicos = [new ItemDeServicoMensagemBuilder().Build()];
    private List<ItemDePecaMensagem> _pecas = [new ItemDePecaMensagemBuilder().Build()];

    public IniciarExecucaoBuilder ComOrdemDeServicoId(Guid ordemDeServicoId) { _ordemDeServicoId = ordemDeServicoId; return this; }
    public IniciarExecucaoBuilder ComServicos(params ItemDeServicoMensagem[] servicos) { _servicos = servicos.ToList(); return this; }
    public IniciarExecucaoBuilder ComPecas(params ItemDePecaMensagem[] pecas) { _pecas = pecas.ToList(); return this; }

    public IniciarExecucao Build() =>
        new(Guid.NewGuid(), _ordemDeServicoId, Faker.Date.RecentOffset(), _numero, _servicos, _pecas);
}
