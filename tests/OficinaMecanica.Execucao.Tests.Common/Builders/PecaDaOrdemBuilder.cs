using Bogus;
using OficinaMecanica.Execucao.Domain.ValueObjects;

namespace OficinaMecanica.Execucao.Tests.Common.Builders;

public class PecaDaOrdemBuilder
{
    private static readonly Faker Faker = new("pt_BR");
    private static readonly string[] Nomes = ["Filtro de óleo", "Pastilha de freio", "Correia dentada", "Vela de ignição", "Amortecedor"];

    private Guid _pecaId = Guid.NewGuid();
    private string _nome = Faker.PickRandom(Nomes);
    private int _quantidade = Faker.Random.Int(1, 4);
    private decimal _precoUnitario = Faker.Random.Decimal(10, 300);

    public PecaDaOrdemBuilder ComPecaId(Guid pecaId) { _pecaId = pecaId; return this; }
    public PecaDaOrdemBuilder ComNome(string nome) { _nome = nome; return this; }
    public PecaDaOrdemBuilder ComQuantidade(int quantidade) { _quantidade = quantidade; return this; }
    public PecaDaOrdemBuilder ComPrecoUnitario(decimal precoUnitario) { _precoUnitario = precoUnitario; return this; }

    public PecaDaOrdem Build() => PecaDaOrdem.Criar(_pecaId, _nome, _quantidade, _precoUnitario);
}
