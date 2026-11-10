using OficinaMecanica.Execucao.Domain.Common;
using OficinaMecanica.Execucao.Domain.Exceptions;

namespace OficinaMecanica.Execucao.Domain.ValueObjects;

public sealed class Apontamento : ValueObject
{
    public string MecanicoId { get; }
    public string Texto { get; }
    public DateTimeOffset Em { get; }

    private Apontamento(string mecanicoId, string texto, DateTimeOffset em)
    {
        MecanicoId = mecanicoId;
        Texto = texto;
        Em = em;
    }

    public static Apontamento Criar(string mecanicoId, string texto, DateTimeOffset em)
    {
        if (string.IsNullOrWhiteSpace(mecanicoId))
            throw new DomainException("O mecânico do apontamento é obrigatório.");

        if (string.IsNullOrWhiteSpace(texto))
            throw new DomainException("O texto do apontamento é obrigatório.");

        return new Apontamento(mecanicoId, texto.Trim(), em);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return MecanicoId;
        yield return Texto;
        yield return Em;
    }
}
