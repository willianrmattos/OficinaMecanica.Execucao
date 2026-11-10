using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OficinaMecanica.Execucao.Application.Behaviors;

namespace OficinaMecanica.Execucao.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly);

        // Optei por TryAdd para o host (ou um teste) poder trocar o relogio; os handlers leem o "agora"
        // daqui, nunca de DateTime, e passam o valor para o dominio.
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
