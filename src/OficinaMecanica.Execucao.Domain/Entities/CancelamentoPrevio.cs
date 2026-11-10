using OficinaMecanica.Execucao.Domain.Common;
using OficinaMecanica.Execucao.Domain.Exceptions;

namespace OficinaMecanica.Execucao.Domain.Entities;

// Optei por registrar um "tombstone" quando o cancelamento chega antes da ordem existir: as filas
// reordenam e reentregam, e sem esse registro o SolicitarDiagnostico atrasado enfileiraria justamente
// a ordem que a saga ja mandou desfazer. O Id e o proprio OrdemDeServicoId, o que da unicidade (e
// idempotencia do registro) sem consulta extra.
public class CancelamentoPrevio : Entity
{
    public Guid OrdemDeServicoId => Id;
    public string Motivo { get; private set; }
    public DateTimeOffset RegistradoEm { get; private set; }

    public CancelamentoPrevio(Guid ordemDeServicoId, string motivo, DateTimeOffset registradoEm)
        : base(ordemDeServicoId)
    {
        if (ordemDeServicoId == Guid.Empty)
            throw new DomainException("A ordem de serviço do cancelamento prévio é obrigatória.");

        if (string.IsNullOrWhiteSpace(motivo))
            throw new DomainException("O motivo do cancelamento prévio é obrigatório.");

        Motivo = motivo.Trim();
        RegistradoEm = registradoEm;
    }
}
