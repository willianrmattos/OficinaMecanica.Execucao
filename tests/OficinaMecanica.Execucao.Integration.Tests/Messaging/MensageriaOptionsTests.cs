using Microsoft.Extensions.Configuration;
using OficinaMecanica.Execucao.Infrastructure.Messaging;

namespace OficinaMecanica.Execucao.Integration.Tests.Messaging;

public class MensageriaOptionsTests
{
    private static IConfiguration Configuracao(Dictionary<string, string?> valores) =>
        new ConfigurationBuilder().AddInMemoryCollection(valores).Build();

    [Fact]
    public void IntervalosDeRetentativa_DevemDobrarAPartirDoInicialELimitarAoMaximo()
    {
        var opcoes = new MensageriaOptions { Retentativas = 6, IntervaloInicialEmMs = 200, IntervaloMaximoEmMs = 1_000 };

        opcoes.IntervalosDeRetentativa().Select(i => i.TotalMilliseconds)
            .Should().Equal(200, 400, 800, 1_000, 1_000, 1_000);
    }

    [Fact]
    public void IntervalosDeRetentativa_SemRetentativas_DeveSerVazio()
    {
        new MensageriaOptions { Retentativas = 0 }.IntervalosDeRetentativa().Should().BeEmpty();
    }

    [Fact]
    public void Carregar_SemSecao_DeveUsarOsPadroesDeProducao()
    {
        var opcoes = MensageriaOptions.Carregar(Configuracao([]));

        opcoes.Transporte.Should().Be(TransporteDeMensagens.RabbitMq);
        opcoes.Outbox.Should().Be(OutboxDeMensagens.MongoDb);
        opcoes.Retentativas.Should().Be(5);
        opcoes.IntervaloDoOutboxEmMs.Should().Be(1_000);
        opcoes.JanelaDeDeduplicacaoEmMinutos.Should().Be(30);
    }

    [Fact]
    public void Carregar_ComSecao_DeveLerOsPontosDeTrocaDeTransporteEPersistencia()
    {
        var opcoes = MensageriaOptions.Carregar(Configuracao(new()
        {
            ["Mensageria:Transporte"] = "EmMemoria",
            ["Mensageria:Outbox"] = "Nenhum",
            ["Mensageria:Retentativas"] = "2"
        }));

        opcoes.Transporte.Should().Be(TransporteDeMensagens.EmMemoria);
        opcoes.Outbox.Should().Be(OutboxDeMensagens.Nenhum);
        opcoes.Retentativas.Should().Be(2);
    }

    [Theory]
    [InlineData("Transporte", "0", true)]
    [InlineData("Transporte", "1", false)]
    public void Carregar_ComNumeroDefinido_DeveSelecionarOTransporteCorreto(string chave, string valor, bool rabbitMq)
    {
        var opcoes = MensageriaOptions.Carregar(Configuracao(new() { [$"Mensageria:{chave}"] = valor }));

        opcoes.UsaRabbitMq.Should().Be(rabbitMq);
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("1", false)]
    public void Carregar_ComNumeroDefinidoParaOutbox_DeveSelecionarOOutboxCorreto(string valor, bool outboxDoMongo)
    {
        var opcoes = MensageriaOptions.Carregar(Configuracao(new() { ["Mensageria:Outbox"] = valor }));

        opcoes.UsaOutboxDoMongo.Should().Be(outboxDoMongo);
        opcoes.Outbox.Should().Be(outboxDoMongo ? OutboxDeMensagens.MongoDb : OutboxDeMensagens.Nenhum);
    }

    [Theory]
    [InlineData("Transporte", "2")]
    [InlineData("Transporte", "9")]
    [InlineData("Transporte", "-1")]
    [InlineData("Outbox", "2")]
    [InlineData("Outbox", "9")]
    [InlineData("Outbox", "-1")]
    public void Carregar_ComNumeroForaDoEnum_DeveLancarEmVezDeCairEmMemoria(string chave, string valor)
    {
        var acao = () => MensageriaOptions.Carregar(Configuracao(new() { [$"Mensageria:{chave}"] = valor }));

        acao.Should().Throw<InvalidOperationException>().WithMessage($"Mensageria:{chave}={valor} não é um valor válido*");
    }

    [Theory]
    [InlineData("Transporte")]
    [InlineData("Outbox")]
    public void Carregar_ComTextoInvalido_DeveLancar(string chave)
    {
        var acao = () => MensageriaOptions.Carregar(Configuracao(new() { [$"Mensageria:{chave}"] = "abc" }));

        acao.Should().Throw<InvalidOperationException>().WithMessage($"*Mensageria:{chave}*");
    }

    [Fact]
    public void UsaRabbitMqEUsaOutboxDoMongo_ComValorIndefinido_DevemLancar()
    {
        var opcoes = new MensageriaOptions { Transporte = (TransporteDeMensagens)7, Outbox = (OutboxDeMensagens)7 };

        ((Func<bool>)(() => opcoes.UsaRabbitMq)).Should().Throw<InvalidOperationException>().WithMessage("*Transporte=7*");
        ((Func<bool>)(() => opcoes.UsaOutboxDoMongo)).Should().Throw<InvalidOperationException>().WithMessage("*Outbox=7*");
    }

    [Fact]
    public void CarregarRabbitMq_DeveLerHostPortaVirtualHostEUsuario()
    {
        var rabbit = MensageriaOptions.CarregarRabbitMq(Configuracao(new()
        {
            ["RabbitMq:Host"] = "rabbitmq.messaging.svc",
            ["RabbitMq:Port"] = "5673",
            ["RabbitMq:VirtualHost"] = "execucao",
            ["RabbitMq:Username"] = "svc-execucao",
            ["RabbitMq:Password"] = "valor-de-teste"
        }));

        rabbit.Host.Should().Be("rabbitmq.messaging.svc");
        rabbit.Port.Should().Be(5673);
        rabbit.VirtualHost.Should().Be("execucao");
        rabbit.Username.Should().Be("svc-execucao");
        rabbit.Password.Should().Be("valor-de-teste");
    }

    [Fact]
    public void CarregarRabbitMq_SemSecao_NaoDeveTrazerCredencialPadrao()
    {
        var rabbit = MensageriaOptions.CarregarRabbitMq(Configuracao([]));

        rabbit.Username.Should().BeEmpty();
        rabbit.Password.Should().BeEmpty();
        rabbit.Port.Should().Be(5672);
        rabbit.VirtualHost.Should().Be("/");
    }
}
