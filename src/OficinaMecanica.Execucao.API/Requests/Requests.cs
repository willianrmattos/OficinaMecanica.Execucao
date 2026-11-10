namespace OficinaMecanica.Execucao.API.Requests;

/// <summary>Anotação de trabalho feita pelo mecânico responsável.</summary>
/// <param name="Texto">Texto livre do apontamento.</param>
public record RegistrarApontamentoRequest(string Texto);

/// <summary>Conclusão do diagnóstico com o laudo.</summary>
/// <param name="Laudo">Texto do laudo.</param>
/// <param name="HorasEstimadas">Horas estimadas para o reparo, quando o mecânico souber.</param>
public record ConcluirDiagnosticoRequest(string Laudo, decimal? HorasEstimadas = null);

/// <summary>Finalização do reparo.</summary>
/// <param name="Resumo">Resumo do que foi executado.</param>
public record FinalizarReparoRequest(string Resumo);

/// <summary>Inviabilização da execução.</summary>
/// <param name="Motivo">Por que o reparo não pode ser feito.</param>
public record InviabilizarReparoRequest(string Motivo);
