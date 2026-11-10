using MediatR;
using OficinaMecanica.Execucao.Application.DTOs;

namespace OficinaMecanica.Execucao.Application.Commands.InviabilizarExecucao;

public record InviabilizarExecucaoCommand(Guid OrdemDeServicoId, string MecanicoId, string Motivo) : IRequest<OrdemDeProducaoDto>;
