using MediatR;
using OficinaMecanica.Execucao.Application.DTOs;

namespace OficinaMecanica.Execucao.Application.Commands.ReivindicarProximoDiagnostico;

public record ReivindicarProximoDiagnosticoCommand(string MecanicoId) : IRequest<ResultadoDaReivindicacaoDto>;
