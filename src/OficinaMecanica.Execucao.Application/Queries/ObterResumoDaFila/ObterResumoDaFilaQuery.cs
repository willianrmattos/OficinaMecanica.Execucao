using MediatR;
using OficinaMecanica.Execucao.Application.DTOs;

namespace OficinaMecanica.Execucao.Application.Queries.ObterResumoDaFila;

public record ObterResumoDaFilaQuery : IRequest<ResumoDaFilaDto>;
