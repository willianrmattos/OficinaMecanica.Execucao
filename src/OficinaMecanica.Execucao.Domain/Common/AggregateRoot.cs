namespace OficinaMecanica.Execucao.Domain.Common;

public abstract class AggregateRoot : Entity
{
    public long Version { get; protected set; }

    protected AggregateRoot(Guid id) : base(id) { }

    // Deixei publico porque quem incrementa e a implementacao do repositorio, que vive em outro assembly
    // (Infrastructure) e so pode faze-lo depois de gravar com sucesso.
    public void IncrementVersion() => Version++;
}
