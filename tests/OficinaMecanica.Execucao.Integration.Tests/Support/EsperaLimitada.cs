using FluentAssertions.Execution;

namespace OficinaMecanica.Execucao.Integration.Tests.Support;

// Teto de espera para os testes que aguardam uma tarefa que o codigo sob teste pode deixar de liberar (por exemplo um
// TaskCompletionSource que so o limite do check conclui). Sem o teto, remover a protecao que se quer provar faz o teste
// pendurar em silencio em vez de falhar, e o run so acaba no timeout do job de CI.
public static class EsperaLimitada
{
    // Dobro do maior limite do codigo sob teste (3 s): acima de qualquer espera legitima e curto o bastante para a falha
    // aparecer logo, mesmo quando varios testes pendurados somam o teto em sequencia.
    public static readonly TimeSpan Teto = TimeSpan.FromSeconds(6);

    // Task.WhenAny (e nao WaitAsync) para que uma TimeoutException lancada pela propria tarefa nao seja confundida com o teto.
    public static async Task ComTetoAsync(this Task tarefa, string oQueEraEsperado)
    {
        var primeira = await Task.WhenAny(tarefa, Task.Delay(Teto));
        if (!ReferenceEquals(primeira, tarefa))
            Execute.Assertion.FailWith(
                "A operacao nao terminou em {0} s (travou em vez de falhar): {1}", Teto.TotalSeconds, oQueEraEsperado);

        await tarefa;
    }

    public static async Task<T> ComTetoAsync<T>(this Task<T> tarefa, string oQueEraEsperado)
    {
        await ((Task)tarefa).ComTetoAsync(oQueEraEsperado);
        return await tarefa;
    }
}
