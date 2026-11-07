using MediatR;
using OficinaMecanica.Execucao.Application.DTOs;

namespace OficinaMecanica.Execucao.Application.Commands.FinalizarReparo;

public record FinalizarReparoCommand(Guid OrdemDeServicoId, string MecanicoId, string Resumo) : IRequest<OrdemDeProducaoDto>;
