using Microsoft.AspNetCore.Mvc;
using OficinaMecanica.Execucao.API.Middleware;

namespace OficinaMecanica.Execucao.API.Extensions;

public static class ControllersExtensions
{
    public static IServiceCollection AddControllersDoExecucao(this IServiceCollection services)
    {
        services
            .AddControllers(opcoes =>
            {
                // A obrigatoriedade dos campos e validada pelo FluentValidation (mensagens em portugues e no
                // mesmo formato de erro); sem isto o MVC rejeitaria antes, com outro formato.
                opcoes.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
            })
            .ConfigureApiBehaviorOptions(opcoes =>
            {
                // Corpo ausente ou JSON malformado tambem respondem no formato ErrorResponse.
                opcoes.InvalidModelStateResponseFactory = contexto =>
                {
                    var erros = contexto.ModelState
                        .Where(entrada => entrada.Value is { Errors.Count: > 0 })
                        .SelectMany(entrada => entrada.Value!.Errors.Select(e => new ErrorDetail(
                            entrada.Key,
                            string.IsNullOrWhiteSpace(e.ErrorMessage) ? "Valor inválido." : e.ErrorMessage)))
                        .ToList();

                    return new BadRequestObjectResult(new ErrorResponse
                    {
                        Tipo = "Erro de Validação",
                        Mensagem = "Erro de validação.",
                        Erros = erros
                    });
                };
            });

        return services;
    }
}
