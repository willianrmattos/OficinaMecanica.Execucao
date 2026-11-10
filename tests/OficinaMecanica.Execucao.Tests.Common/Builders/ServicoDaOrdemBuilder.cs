using Bogus;
using OficinaMecanica.Execucao.Domain.ValueObjects;

namespace OficinaMecanica.Execucao.Tests.Common.Builders;

public class ServicoDaOrdemBuilder
{
    private static readonly Faker Faker = new("pt_BR");
    private static readonly string[] Nomes = ["Troca de óleo", "Alinhamento", "Balanceamento", "Revisão de freios", "Troca de correia"];

    private Guid _servicoId = Guid.NewGuid();
    private string _nome = Faker.PickRandom(Nomes);
    private int _quantidade = Faker.Random.Int(1, 3);
    private decimal _precoUnitario = Faker.Random.Decimal(50, 500);

    public ServicoDaOrdemBuilder ComServicoId(Guid servicoId) { _servicoId = servicoId; return this; }
    public ServicoDaOrdemBuilder ComNome(string nome) { _nome = nome; return this; }
    public ServicoDaOrdemBuilder ComQuantidade(int quantidade) { _quantidade = quantidade; return this; }
    public ServicoDaOrdemBuilder ComPrecoUnitario(decimal precoUnitario) { _precoUnitario = precoUnitario; return this; }

    public ServicoDaOrdem Build() => ServicoDaOrdem.Criar(_servicoId, _nome, _quantidade, _precoUnitario);
}
