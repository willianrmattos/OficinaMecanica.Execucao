using MediatR;
using OficinaMecanica.Execucao.Application.DTOs;

namespace OficinaMecanica.Execucao.Application.Commands.ReivindicarProximoReparo;

public record ReivindicarProximoReparoCommand(string MecanicoId) : IRequest<ResultadoDaReivindicacaoDto>;
