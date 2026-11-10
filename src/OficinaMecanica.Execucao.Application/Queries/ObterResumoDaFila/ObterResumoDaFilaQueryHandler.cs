using MediatR;
using OficinaMecanica.Execucao.Application.DTOs;
using OficinaMecanica.Execucao.Domain.Enums;
using OficinaMecanica.Execucao.Domain.Interfaces;

namespace OficinaMecanica.Execucao.Application.Queries.ObterResumoDaFila;

public class ObterResumoDaFilaQueryHandler : IRequestHandler<ObterResumoDaFilaQuery, ResumoDaFilaDto>
{
    private readonly IOrdemDeProducaoRepository _repositorio;

    public ObterResumoDaFilaQueryHandler(IOrdemDeProducaoRepository repositorio)
    {
        _repositorio = repositorio;
    }

    public async Task<ResumoDaFilaDto> Handle(ObterResumoDaFilaQuery request, CancellationToken cancellationToken)
    {
        var contagem = await _repositorio.ContarPorStatusAsync(cancellationToken);

        // Optei por devolver todos os status, mesmo com zero, para quem consome o painel nao ter de
        // tratar chave ausente.
        var porStatus = Enum.GetValues<StatusProducao>()
            .ToDictionary(status => status.ToString(), status => contagem.GetValueOrDefault(status));

        return new ResumoDaFilaDto(porStatus, porStatus.Values.Sum());
    }
}
