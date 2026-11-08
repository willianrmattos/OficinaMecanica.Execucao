using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using OficinaMecanica.Execucao.API.Extensions;
using OficinaMecanica.Execucao.API.Health;
using OficinaMecanica.Execucao.API.Middleware;
using OficinaMecanica.Execucao.Application;
using OficinaMecanica.Execucao.Infrastructure;
using Prometheus;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration().ConfigurarSerilog(builder.Configuration).CreateLogger();
builder.Host.UseSerilog();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddObservabilidade(builder.Configuration);
builder.Services.AddControllersDoExecucao();
builder.Services.AddSwaggerDoExecucao();

builder.Services.AddHealthChecks();

var app = builder.Build();

// Scheme e host (X-Forwarded-Proto/-Host) so sao confiaveis com este middleware. KnownNetworks/KnownProxies
// limpos porque o IP de quem repassa (o ingress-nginx) nao e fixo.
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
};
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

// A APIM manda o prefixo da rota em X-Forwarded-Prefix; sem ajustar o PathBase, os links absolutos do
// Swagger saem sem ele.
app.Use((context, next) =>
{
    var prefixo = context.Request.Headers["X-Forwarded-Prefix"].FirstOrDefault();
    if (!string.IsNullOrEmpty(prefixo))
        context.Request.PathBase = new PathString(prefixo);
    return next();
});

// As metricas HTTP ficam por fora do middleware de excecoes para ler o status que ele define (499 de cliente que
// abandonou, 400/404/409 e 500); por dentro, o prometheus-net veria o 200 padrao e contaria todo erro como sucesso.
app.UseHttpMetrics();
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseSwaggerDoExecucao();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapMetrics();
// /health so confirma que o processo esta de pe (liveness, sem dependencias: queda de dependencia nao pode
// reiniciar o pod). /health/ready (tag "ready") cobre as dependencias: o MongoDB, sem o qual o servico nao aceita
// escrita, devolve 503 quando cai e tira o pod de rotacao; o broker (conexao AMQP real e o check do bus do
// MassTransit) devolve Degraded, que continua 200, porque o outbox segura as mensagens ate o RabbitMQ voltar e
// readiness em cascata viraria indisponibilidade total. O alerta do broker fica por metrica
// (outbox_mensagens_pendentes).
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registro => registro.Tags.Contains("ready"),
    ResponseWriter = ProntidaoResponseWriter.EscreverAsync
});

Log.Information("OficinaMecanica.Execucao API iniciada com sucesso.");

app.Run();

// Necessario para os testes de integracao (WebApplicationFactory).
public partial class Program { }
