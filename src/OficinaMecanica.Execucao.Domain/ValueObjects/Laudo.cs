using OficinaMecanica.Execucao.Domain.Common;
using OficinaMecanica.Execucao.Domain.Exceptions;

namespace OficinaMecanica.Execucao.Domain.ValueObjects;

public sealed class Laudo : ValueObject
{
    public string Texto { get; }
    public decimal? HorasEstimadas { get; }

    private Laudo(string texto, decimal? horasEstimadas)
    {
        Texto = texto;
        HorasEstimadas = horasEstimadas;
    }

    public static Laudo Criar(string texto, decimal? horasEstimadas = null)
    {
        if (string.IsNullOrWhiteSpace(texto))
            throw new DomainException("O texto do laudo é obrigatório.");

        if (horasEstimadas.HasValue && horasEstimadas.Value <= 0)
            throw new DomainException("As horas estimadas do laudo devem ser maiores que zero.");

        return new Laudo(texto.Trim(), horasEstimadas);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Texto;
        yield return HorasEstimadas;
    }
}
