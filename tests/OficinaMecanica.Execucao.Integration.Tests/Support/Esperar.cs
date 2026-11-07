namespace OficinaMecanica.Execucao.Integration.Tests.Support;

public static class Esperar
{
    // Espera uma condicao assincrona ate o limite. Os testes de infra convergem por mensageria e nao devem
    // depender de Task.Delay fixo.
    public static async Task<bool> AteAsync(Func<Task<bool>> condicao, int limiteEmMs = 20_000, int intervaloEmMs = 100)
    {
        var fim = DateTime.UtcNow.AddMilliseconds(limiteEmMs);

        while (DateTime.UtcNow < fim)
        {
            if (await condicao())
                return true;

            await Task.Delay(intervaloEmMs);
        }

        return await condicao();
    }

    public static Task<bool> AteAsync(Func<bool> condicao, int limiteEmMs = 20_000, int intervaloEmMs = 100) =>
        AteAsync(() => Task.FromResult(condicao()), limiteEmMs, intervaloEmMs);
}
