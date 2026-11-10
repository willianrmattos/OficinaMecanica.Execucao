namespace OficinaMecanica.Execucao.Application.DTOs;

// Optei por um resultado explicito para fila vazia: e um desfecho esperado do mecanico pedir a
// proxima ordem, nao um erro, e um null solto deixaria a API adivinhar o que significa.
public record ResultadoDaReivindicacaoDto(bool FilaVazia, OrdemDeProducaoDto? Ordem)
{
    public static ResultadoDaReivindicacaoDto Vazia() => new(true, null);

    public static ResultadoDaReivindicacaoDto Reivindicada(OrdemDeProducaoDto ordem) => new(false, ordem);
}
