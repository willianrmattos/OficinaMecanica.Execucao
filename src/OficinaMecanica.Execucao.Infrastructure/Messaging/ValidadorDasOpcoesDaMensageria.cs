using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace OficinaMecanica.Execucao.Infrastructure.Messaging;

// Falha a partida (ValidateOnStart) quando o transporte em memoria ou a ausencia de outbox sao pedidos fora de
// Development e Testing: sem o RabbitMQ e o outbox do Mongo o servico sobe "saudavel" mas perde as mensagens em
// silencio e deixa de ser atomico com o banco. Os ambientes reais (Staging, Production, o do k8s) exigem os dois.
public sealed class ValidadorDasOpcoesDaMensageria(IHostEnvironment ambiente, IOptions<RabbitMqOptions> rabbitMq)
    : IValidateOptions<MensageriaOptions>
{
    public ValidateOptionsResult Validate(string? name, MensageriaOptions options)
    {
        var falhas = new List<string>();
        var permiteAtalhos = ambiente.IsDevelopment() || ambiente.IsEnvironment("Testing");

        // Primeiro e em qualquer ambiente: um numero fora do enum nao e "EmMemoria" nem "RabbitMq" e as comparacoes
        // abaixo o deixariam passar.
        if (!Enum.IsDefined(options.Transporte))
            falhas.Add(MensageriaOptions.ValorIndefinido("Transporte", options.Transporte).Message);

        if (!Enum.IsDefined(options.Outbox))
            falhas.Add(MensageriaOptions.ValorIndefinido("Outbox", options.Outbox).Message);

        if (options.Transporte == TransporteDeMensagens.EmMemoria && !permiteAtalhos)
            falhas.Add($"Mensageria:Transporte=EmMemoria só é permitido em Development ou Testing (ambiente atual: {ambiente.EnvironmentName}); use RabbitMq.");

        if (options.Outbox == OutboxDeMensagens.Nenhum && !permiteAtalhos)
            falhas.Add($"Mensageria:Outbox=Nenhum só é permitido em Development ou Testing (ambiente atual: {ambiente.EnvironmentName}); use MongoDb.");

        if (options.Transporte == TransporteDeMensagens.RabbitMq)
        {
            var rabbit = rabbitMq.Value;

            if (string.IsNullOrWhiteSpace(rabbit.Host))
                falhas.Add("RabbitMq:Host é obrigatório com o transporte RabbitMq.");

            if (string.IsNullOrWhiteSpace(rabbit.Username) || string.IsNullOrWhiteSpace(rabbit.Password))
                falhas.Add("RabbitMq:Username e RabbitMq:Password são obrigatórios com o transporte RabbitMq.");
        }

        if (options.Retentativas < 0)
            falhas.Add("Mensageria:Retentativas não pode ser negativa.");

        if (options.IntervaloInicialEmMs <= 0 || options.IntervaloMaximoEmMs < options.IntervaloInicialEmMs)
            falhas.Add("Mensageria:IntervaloInicialEmMs deve ser maior que zero e não maior que IntervaloMaximoEmMs.");

        if (options.IntervaloDoOutboxEmMs <= 0)
            falhas.Add("Mensageria:IntervaloDoOutboxEmMs deve ser maior que zero.");

        return falhas.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(falhas);
    }
}
