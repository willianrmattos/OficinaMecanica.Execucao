using MediatR;
using OficinaMecanica.Execucao.Application.DTOs;

namespace OficinaMecanica.Execucao.Application.Queries.ObterOrdemDeProducao;

public record ObterOrdemDeProducaoPorIdQuery(Guid Id) : IRequest<OrdemDeProducaoDto?>;
