using MediatR;
using OficinaMecanica.Execucao.Application.DTOs;

namespace OficinaMecanica.Execucao.Application.Commands.ConcluirDiagnostico;

public record ConcluirDiagnosticoCommand(
    Guid OrdemDeServicoId,
    string MecanicoId,
    string Laudo,
    decimal? HorasEstimadas = null
) : IRequest<OrdemDeProducaoDto>;
