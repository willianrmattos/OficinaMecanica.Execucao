using MediatR;
using OficinaMecanica.Execucao.Application.DTOs;

namespace OficinaMecanica.Execucao.Application.Commands.RegistrarApontamento;

public record RegistrarApontamentoCommand(Guid OrdemDeServicoId, string MecanicoId, string Texto) : IRequest<OrdemDeProducaoDto>;
