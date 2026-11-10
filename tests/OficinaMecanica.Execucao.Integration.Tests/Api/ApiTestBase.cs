using OficinaMecanica.Execucao.Integration.Tests.Support;

namespace OficinaMecanica.Execucao.Integration.Tests.Api;

// Cada teste sobe a propria API (fakes novos), entao as filas e os eventos nao vazam entre cenarios.
public abstract class ApiTestBase : IDisposable
{
    protected ApiSemInfraFactory Api { get; }

    protected ApiTestBase(Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>? ajustes = null)
    {
        Api = new ApiSemInfraFactory(ajustes);
    }

    public void Dispose()
    {
        Api.Dispose();
        GC.SuppressFinalize(this);
    }
}
