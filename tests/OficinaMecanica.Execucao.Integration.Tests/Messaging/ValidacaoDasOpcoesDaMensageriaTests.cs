using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OficinaMecanica.Execucao.Infrastructure.Messaging;
using OficinaMecanica.Execucao.Integration.Tests.Support;

namespace OficinaMecanica.Execucao.Integration.Tests.Messaging;

public class ValidadorDasOpcoesDaMensageriaTests
{
    private sealed class AmbienteFalso(string nome) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = nome;
        public string ApplicationName { get; set; } = "teste";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static RabbitMqOptions RabbitCompleto() =>
        new() { Host = "rabbitmq", Username = "svc-execucao", Password = "valor-de-teste" };

    private static ValidateOptionsResult Validar(string ambiente, MensageriaOptions opcoes, RabbitMqOptions? rabbit = null) =>
        new ValidadorDasOpcoesDaMensageria(new AmbienteFalso(ambiente), Options.Create(rabbit ?? RabbitCompleto()))
            .Validate(null, opcoes);

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("kubernetes")]
    public void TransporteEmMemoria_ForaDeDevelopmentETesting_DeveFalhar(string ambiente)
    {
        var resultado = Validar(ambiente, new MensageriaOptions { Transporte = TransporteDeMensagens.EmMemoria });

        resultado.Failed.Should().BeTrue();
        resultado.Failures.Should().ContainSingle().Which.Should().Contain("EmMemoria").And.Contain(ambiente);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void OutboxNenhum_ForaDeDevelopmentETesting_DeveFalhar(string ambiente)
    {
        var resultado = Validar(ambiente, new MensageriaOptions { Outbox = OutboxDeMensagens.Nenhum });

        resultado.Failed.Should().BeTrue();
        resultado.Failures.Should().ContainSingle().Which.Should().Contain("Outbox=Nenhum");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void AtalhosDeTeste_EmDevelopmentETesting_DevemSerPermitidos(string ambiente)
    {
        var resultado = Validar(ambiente, new MensageriaOptions { Transporte = TransporteDeMensagens.EmMemoria, Outbox = OutboxDeMensagens.Nenhum });

        resultado.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void TransporteForaDoEnum_DeveFalharEmQualquerAmbiente(string ambiente)
    {
        var resultado = Validar(ambiente, new MensageriaOptions { Transporte = (TransporteDeMensagens)9 });

        resultado.Failed.Should().BeTrue();
        resultado.Failures.Should().Contain(f => f.Contains("Mensageria:Transporte=9") && f.Contains("não é um valor válido"));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void OutboxForaDoEnum_DeveFalharEmQualquerAmbiente(string ambiente)
    {
        var resultado = Validar(ambiente, new MensageriaOptions { Outbox = (OutboxDeMensagens)9 });

        resultado.Failed.Should().BeTrue();
        resultado.Failures.Should().Contain(f => f.Contains("Mensageria:Outbox=9") && f.Contains("não é um valor válido"));
    }

    [Fact]
    public void ProducaoComRabbitMqEOutboxDoMongo_DeveSerValida()
    {
        Validar("Production", new MensageriaOptions()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void TransporteRabbitMq_SemCredenciaisOuHost_DeveFalharEmQualquerAmbiente()
    {
        var resultado = Validar("Development", new MensageriaOptions(), new RabbitMqOptions { Host = " " });

        resultado.Failures.Should().HaveCount(2);
        resultado.Failures.Should().Contain(f => f.Contains("RabbitMq:Host"));
        resultado.Failures.Should().Contain(f => f.Contains("RabbitMq:Username"));
    }

    [Fact]
    public void TransporteEmMemoria_NaoExigeCredenciaisDoRabbit()
    {
        var resultado = Validar("Testing", new MensageriaOptions { Transporte = TransporteDeMensagens.EmMemoria }, new RabbitMqOptions());

        resultado.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void IntervalosEmRetentativasInvalidos_DevemFalhar()
    {
        var resultado = Validar("Production", new MensageriaOptions
        {
            Retentativas = -1,
            IntervaloInicialEmMs = 500,
            IntervaloMaximoEmMs = 100,
            IntervaloDoOutboxEmMs = 0
        });

        resultado.Failures.Should().HaveCount(3);
    }
}

public class PartidaDoServicoTests
{
    // Producao com o transporte em memoria subiria "saudavel" e perderia as mensagens em silencio: a partida
    // precisa falhar (ValidateOnStart), antes de abrir qualquer conexao.
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void AmbienteReal_ComTransporteEmMemoria_DeveFalharAPartida(string ambiente)
    {
        using var api = new ApiSemInfraFactory(ambiente: ambiente);

        var partida = () => api.Server;

        var erro = partida.Should().Throw<Exception>().Which;
        erro.ToString().Should().Contain("OptionsValidationException").And.Contain("Mensageria:Transporte=EmMemoria");
    }

    [Fact]
    public void AmbienteReal_ComOutboxNenhum_DeveFalharAPartida()
    {
        using var api = new ApiSemInfraFactory(
            ambiente: "Production",
            configuracao: new Dictionary<string, string> { ["Mensageria:Transporte"] = "RabbitMq", ["RabbitMq:Username"] = "svc", ["RabbitMq:Password"] = "x" });

        var partida = () => api.Server;

        partida.Should().Throw<Exception>().Which.ToString().Should().Contain("Mensageria:Outbox=Nenhum");
    }

    private static IReadOnlyDictionary<string, string> ConfiguracaoReal(string chave, string valor) =>
        new Dictionary<string, string>
        {
            ["Mensageria:Transporte"] = "RabbitMq",
            ["Mensageria:Outbox"] = "MongoDb",
            ["RabbitMq:Username"] = "svc",
            ["RabbitMq:Password"] = "x",
            [$"Mensageria:{chave}"] = valor
        };

    // Fail-closed: o binder aceita qualquer numero para o enum e um "senao, em memoria" deixaria o valor desconhecido
    // passar para o transporte em memoria sem outbox. Cada caso varia UMA chave, com a outra valida.
    [Theory]
    [InlineData("Production", "Transporte", "9")]
    [InlineData("Development", "Transporte", "9")]
    [InlineData("Production", "Transporte", "-1")]
    [InlineData("Production", "Outbox", "9")]
    [InlineData("Development", "Outbox", "9")]
    [InlineData("Production", "Outbox", "-1")]
    public void NumeroForaDoEnum_DeveFalharAPartidaEmQualquerAmbiente(string ambiente, string chave, string valor)
    {
        using var api = new ApiSemInfraFactory(ambiente: ambiente, configuracao: ConfiguracaoReal(chave, valor));

        var partida = () => api.Server;

        partida.Should().Throw<Exception>().Which.ToString()
            .Should().Contain("System.InvalidOperationException")
            .And.Contain($"Mensageria:{chave}={valor} não é um valor válido");
    }

    [Theory]
    [InlineData("Production", "Transporte", "abc")]
    [InlineData("Development", "Transporte", "abc")]
    [InlineData("Production", "Outbox", "abc")]
    [InlineData("Development", "Outbox", "abc")]
    public void TextoInvalido_DeveFalharAPartidaEmQualquerAmbiente(string ambiente, string chave, string valor)
    {
        using var api = new ApiSemInfraFactory(ambiente: ambiente, configuracao: ConfiguracaoReal(chave, valor));

        var partida = () => api.Server;

        partida.Should().Throw<Exception>().Which.ToString()
            .Should().Contain("System.InvalidOperationException")
            .And.Contain($"Mensageria:{chave}");
    }

    [Fact]
    public void Testing_ComTransporteEmMemoria_DeveSubir()
    {
        using var api = new ApiSemInfraFactory(ambiente: "Testing");

        var partida = () => api.Server;

        partida.Should().NotThrow();
    }
}
