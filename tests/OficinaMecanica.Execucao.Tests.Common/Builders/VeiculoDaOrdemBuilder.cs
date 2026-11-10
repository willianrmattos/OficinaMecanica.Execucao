using Bogus;
using OficinaMecanica.Execucao.Domain.ValueObjects;

namespace OficinaMecanica.Execucao.Tests.Common.Builders;

public class VeiculoDaOrdemBuilder
{
    private static readonly Faker Faker = new("pt_BR");
    private static readonly string[] Marcas = ["Toyota", "Honda", "Chevrolet", "Volkswagen", "Ford"];
    private static readonly string[] Modelos = ["Corolla", "Civic", "Onix", "Golf", "Fiesta"];

    private string _placa = Faker.Random.Replace("???#?##").ToUpperInvariant();
    private string _marca = Faker.PickRandom(Marcas);
    private string _modelo = Faker.PickRandom(Modelos);

    public VeiculoDaOrdemBuilder ComPlaca(string placa) { _placa = placa; return this; }
    public VeiculoDaOrdemBuilder ComMarca(string marca) { _marca = marca; return this; }
    public VeiculoDaOrdemBuilder ComModelo(string modelo) { _modelo = modelo; return this; }

    public VeiculoDaOrdem Build() => VeiculoDaOrdem.Criar(_placa, _marca, _modelo);
}
