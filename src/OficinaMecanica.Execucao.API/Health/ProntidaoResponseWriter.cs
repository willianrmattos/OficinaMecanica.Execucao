using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace OficinaMecanica.Execucao.API.Health;

// Corpo de /health/ready: o status agregado e o de cada check, para quem monitora ver o broker Degraded mesmo com o
// HTTP em 200. Escrevo so a descricao de cada check, nunca a excecao, que traz host e detalhes de conexao.
public static class ProntidaoResponseWriter
{
    private static readonly JsonSerializerOptions Opcoes = new(JsonSerializerDefaults.Web);

    public static Task EscreverAsync(HttpContext context, HealthReport relatorio)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var corpo = new
        {
            status = relatorio.Status.ToString(),
            checks = relatorio.Entries.ToDictionary(
                entrada => entrada.Key,
                entrada => new { status = entrada.Value.Status.ToString(), descricao = entrada.Value.Description })
        };

        return JsonSerializer.SerializeAsync(context.Response.Body, corpo, Opcoes, context.RequestAborted);
    }
}
