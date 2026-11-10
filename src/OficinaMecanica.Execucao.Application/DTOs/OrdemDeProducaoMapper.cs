using OficinaMecanica.Execucao.Domain.Entities;

namespace OficinaMecanica.Execucao.Application.DTOs;

public static class OrdemDeProducaoMapper
{
    public static OrdemDeProducaoDto ToDto(OrdemDeProducao ordem) =>
        new(
            ordem.Id,
            ordem.Numero,
            new VeiculoDaOrdemDto(ordem.Veiculo.Placa, ordem.Veiculo.Marca, ordem.Veiculo.Modelo),
            ordem.Observacoes,
            ordem.Status.ToString(),
            ordem.MecanicoId,
            ordem.Servicos.Select(s => new ServicoDaOrdemDto(s.ServicoId, s.Nome, s.Quantidade, s.PrecoUnitario)).ToList(),
            ordem.Pecas.Select(p => new PecaDaOrdemDto(p.PecaId, p.Nome, p.Quantidade, p.PrecoUnitario)).ToList(),
            ordem.Etapas.Select(e => new EtapaProducaoDto(e.Nome.ToString(), e.MecanicoId, e.Status.ToString(), e.Inicio, e.Fim)).ToList(),
            ordem.Apontamentos.Select(a => new ApontamentoDto(a.MecanicoId, a.Texto, a.Em)).ToList(),
            ordem.Laudo is null ? null : new LaudoDto(ordem.Laudo.Texto, ordem.Laudo.HorasEstimadas),
            ordem.Resumo,
            ordem.MotivoInviabilidade,
            ordem.InviabilizadaPor,
            ordem.MotivoCancelamento,
            ordem.EnfileiradaEm,
            ordem.LiberadaEm
        );

    public static OrdemDeProducaoResumoDto ToResumoDto(OrdemDeProducao ordem) =>
        new(
            ordem.Id,
            ordem.Numero,
            ordem.Veiculo.Placa,
            ordem.Veiculo.Marca,
            ordem.Veiculo.Modelo,
            ordem.Status.ToString(),
            ordem.MecanicoId,
            ordem.EnfileiradaEm
        );
}
