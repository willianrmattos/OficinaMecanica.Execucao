using OficinaMecanica.Execucao.Domain.Common;
using OficinaMecanica.Execucao.Domain.Exceptions;

namespace OficinaMecanica.Execucao.Domain.ValueObjects;

public sealed class ServicoDaOrdem : ValueObject
{
    public Guid ServicoId { get; }
    public string Nome { get; }
    public int Quantidade { get; }
    public decimal PrecoUnitario { get; }

    private ServicoDaOrdem(Guid servicoId, string nome, int quantidade, decimal precoUnitario)
    {
        ServicoId = servicoId;
        Nome = nome;
        Quantidade = quantidade;
        PrecoUnitario = precoUnitario;
    }

    public static ServicoDaOrdem Criar(Guid servicoId, string nome, int quantidade, decimal precoUnitario)
    {
        if (string.IsNullOrWhiteSpace(nome))
            throw new DomainException("O nome do serviço é obrigatório.");

        if (quantidade <= 0)
            throw new DomainException("A quantidade do serviço deve ser maior que zero.");

        if (precoUnitario < 0)
            throw new DomainException("O preço do serviço não pode ser negativo.");

        return new ServicoDaOrdem(servicoId, nome.Trim(), quantidade, precoUnitario);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return ServicoId;
        yield return Nome;
        yield return Quantidade;
        yield return PrecoUnitario;
    }
}
