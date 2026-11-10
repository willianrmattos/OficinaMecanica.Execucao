using Bogus;
using OficinaMecanica.Contracts.Common;

namespace OficinaMecanica.Execucao.Tests.Common.Builders;

public class ItemDePecaMensagemBuilder
{
    private static readonly Faker Faker = new("pt_BR");

    private Guid _pecaId = Guid.NewGuid();
    private string _nome = Faker.Commerce.ProductName();
    private decimal _precoUnitario = Faker.Random.Decimal(10, 300);
    private int _quantidade = Faker.Random.Int(1, 4);

    public ItemDePecaMensagemBuilder ComNome(string nome) { _nome = nome; return this; }
    public ItemDePecaMensagemBuilder ComQuantidade(int quantidade) { _quantidade = quantidade; return this; }
    public ItemDePecaMensagemBuilder ComPrecoUnitario(decimal precoUnitario) { _precoUnitario = precoUnitario; return this; }

    public ItemDePecaMensagem Build() => new(_pecaId, _nome, _precoUnitario, _quantidade);
}
