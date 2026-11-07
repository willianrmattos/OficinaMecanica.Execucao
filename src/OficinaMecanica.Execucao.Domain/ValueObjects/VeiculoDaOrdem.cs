using OficinaMecanica.Execucao.Domain.Common;
using OficinaMecanica.Execucao.Domain.Exceptions;

namespace OficinaMecanica.Execucao.Domain.ValueObjects;

// Optei por guardar so o minimo do veiculo: a Execucao nao precisa de CPF nem de dados do cliente
// para diagnosticar e reparar, entao nao os recebe nem os persiste.
public sealed class VeiculoDaOrdem : ValueObject
{
    public string Placa { get; }
    public string Marca { get; }
    public string Modelo { get; }

    private VeiculoDaOrdem(string placa, string marca, string modelo)
    {
        Placa = placa;
        Marca = marca;
        Modelo = modelo;
    }

    public static VeiculoDaOrdem Criar(string placa, string marca, string modelo)
    {
        if (string.IsNullOrWhiteSpace(placa))
            throw new DomainException("A placa do veículo é obrigatória.");

        if (string.IsNullOrWhiteSpace(marca))
            throw new DomainException("A marca do veículo é obrigatória.");

        if (string.IsNullOrWhiteSpace(modelo))
            throw new DomainException("O modelo do veículo é obrigatório.");

        return new VeiculoDaOrdem(placa.Trim(), marca.Trim(), modelo.Trim());
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Placa;
        yield return Marca;
        yield return Modelo;
    }
}
