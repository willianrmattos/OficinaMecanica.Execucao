using Bogus;
using OficinaMecanica.Execucao.Domain.ValueObjects;

namespace OficinaMecanica.Execucao.Tests.Common.Builders;

public class LaudoBuilder
{
    private static readonly Faker Faker = new("pt_BR");

    private string _texto = Faker.Lorem.Sentence();
    private decimal? _horasEstimadas = Faker.Random.Decimal(1, 20);

    public LaudoBuilder ComTexto(string texto) { _texto = texto; return this; }
    public LaudoBuilder ComHorasEstimadas(decimal? horasEstimadas) { _horasEstimadas = horasEstimadas; return this; }

    public Laudo Build() => Laudo.Criar(_texto, _horasEstimadas);
}
