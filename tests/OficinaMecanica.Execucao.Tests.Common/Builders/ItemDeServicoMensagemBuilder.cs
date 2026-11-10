using Bogus;
using OficinaMecanica.Contracts.Common;

namespace OficinaMecanica.Execucao.Tests.Common.Builders;

public class ItemDeServicoMensagemBuilder
{
    private static readonly Faker Faker = new("pt_BR");

    private Guid _servicoId = Guid.NewGuid();
    private string _nome = Faker.Commerce.ProductName();
    private decimal _precoUnitario = Faker.Random.Decimal(50, 500);
    private int _quantidade = Faker.Random.Int(1, 3);

    public ItemDeServicoMensagemBuilder ComNome(string nome) { _nome = nome; return this; }
    public ItemDeServicoMensagemBuilder ComQuantidade(int quantidade) { _quantidade = quantidade; return this; }
    public ItemDeServicoMensagemBuilder ComPrecoUnitario(decimal precoUnitario) { _precoUnitario = precoUnitario; return this; }

    public ItemDeServicoMensagem Build() => new(_servicoId, _nome, _precoUnitario, _quantidade);
}
