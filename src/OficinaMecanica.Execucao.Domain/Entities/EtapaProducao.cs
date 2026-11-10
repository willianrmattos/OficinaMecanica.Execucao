using OficinaMecanica.Execucao.Domain.Enums;

namespace OficinaMecanica.Execucao.Domain.Entities;

public sealed class EtapaProducao
{
    public NomeEtapa Nome { get; private set; }
    public string MecanicoId { get; private set; }
    public StatusEtapa Status { get; private set; }
    public DateTimeOffset Inicio { get; private set; }
    public DateTimeOffset? Fim { get; private set; }

    private EtapaProducao(NomeEtapa nome, string mecanicoId, DateTimeOffset inicio)
    {
        Nome = nome;
        MecanicoId = mecanicoId;
        Status = StatusEtapa.EmAndamento;
        Inicio = inicio;
    }

    internal static EtapaProducao Iniciar(NomeEtapa nome, string mecanicoId, DateTimeOffset inicio) =>
        new(nome, mecanicoId, inicio);

    internal void Concluir(DateTimeOffset fim)
    {
        Status = StatusEtapa.Concluida;
        Fim = fim;
    }

    internal void Interromper(DateTimeOffset fim)
    {
        Status = StatusEtapa.Interrompida;
        Fim = fim;
    }
}
