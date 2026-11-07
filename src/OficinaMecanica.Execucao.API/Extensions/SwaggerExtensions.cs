using Microsoft.OpenApi.Models;

namespace OficinaMecanica.Execucao.API.Extensions;

public static class SwaggerExtensions
{
    public static IServiceCollection AddSwaggerDoExecucao(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "OficinaMecanica.Execucao API",
                Version = "v1",
                Description = "Fila de execução, diagnóstico e reparo da oficina mecânica. " +
                              "O mecânico é sempre o do token JWT (claim 'sub'), nunca o do corpo da requisição."
            });

            // IDs de operacao unicos por controller+action: varios nomes (ObterPorId, Listar) se repetem entre
            // controllers e a APIM casa o operationId para saber o que atualizar no reimport do OpenAPI.
            c.CustomOperationIds(apiDesc =>
                $"{apiDesc.ActionDescriptor.RouteValues["controller"]}_{apiDesc.ActionDescriptor.RouteValues["action"]}");

            var arquivoXml = Path.Combine(AppContext.BaseDirectory, $"{typeof(SwaggerExtensions).Assembly.GetName().Name}.xml");
            if (File.Exists(arquivoXml))
                c.IncludeXmlComments(arquivoXml);

            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description = "JWT Authorization header usando o esquema Bearer. Exemplo: 'Bearer {token}'",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                Scheme = "Bearer"
            });

            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                    },
                    Array.Empty<string>()
                }
            });
        });

        return services;
    }

    public static WebApplication UseSwaggerDoExecucao(this WebApplication app)
    {
        app.UseSwagger(c =>
        {
            // servers[] calculado por requisicao: reflete o PathBase (X-Forwarded-Prefix da APIM), entao o
            // spec e o "Try it out" apontam para o prefixo certo, direto ou atras do gateway.
            c.PreSerializeFilters.Add((swaggerDoc, httpReq) =>
            {
                swaggerDoc.Servers = new List<OpenApiServer>
                {
                    new() { Url = $"{httpReq.Scheme}://{httpReq.Host}{httpReq.PathBase}" }
                };
            });
        });

        app.UseSwaggerUI(c =>
        {
            // Endpoint relativo (sem "/" inicial): resolve tanto direto quanto com o PathBase da APIM.
            c.SwaggerEndpoint("swagger/v1/swagger.json", "OficinaMecanica.Execucao API v1");
            c.RoutePrefix = string.Empty;
        });

        return app;
    }
}
