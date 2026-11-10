using OficinaMecanica.Execucao.Domain.Common;
using OficinaMecanica.Execucao.Domain.Exceptions;

namespace OficinaMecanica.Execucao.Domain.ValueObjects;

public sealed class PecaDaOrdem : ValueObject
{
    public Guid PecaId { get; }
    public string Nome { get; }
    public int Quantidade { get; }
    public decimal PrecoUnitario { get; }

    private PecaDaOrdem(Guid pecaId, string nome, int quantidade, decimal precoUnitario)
    {
        PecaId = pecaId;
        Nome = nome;
        Quantidade = quantidade;
        PrecoUnitario = precoUnitario;
    }

    public static PecaDaOrdem Criar(Guid pecaId, string nome, int quantidade, decimal precoUnitario)
    {
        if (string.IsNullOrWhiteSpace(nome))
            throw new DomainException("O nome da peça é obrigatório.");

        if (quantidade <= 0)
            throw new DomainException("A quantidade da peça deve ser maior que zero.");

        if (precoUnitario < 0)
            throw new DomainException("O preço da peça não pode ser negativo.");

        return new PecaDaOrdem(pecaId, nome.Trim(), quantidade, precoUnitario);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return PecaId;
        yield return Nome;
        yield return Quantidade;
        yield return PrecoUnitario;
    }
}
