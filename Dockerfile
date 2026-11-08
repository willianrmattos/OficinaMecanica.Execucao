FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore em camada separada: so os .csproj (de TODOS os projetos de src/, inclusive os Contracts) entram
# antes do restore, entao a camada de pacotes so e refeita quando uma dependencia muda.
COPY nuget.config ./
COPY src/OficinaMecanica.Contracts/OficinaMecanica.Contracts.csproj src/OficinaMecanica.Contracts/
COPY src/OficinaMecanica.Execucao.Domain/OficinaMecanica.Execucao.Domain.csproj src/OficinaMecanica.Execucao.Domain/
COPY src/OficinaMecanica.Execucao.Application/OficinaMecanica.Execucao.Application.csproj src/OficinaMecanica.Execucao.Application/
COPY src/OficinaMecanica.Execucao.Infrastructure/OficinaMecanica.Execucao.Infrastructure.csproj src/OficinaMecanica.Execucao.Infrastructure/
COPY src/OficinaMecanica.Execucao.API/OficinaMecanica.Execucao.API.csproj src/OficinaMecanica.Execucao.API/

RUN dotnet restore src/OficinaMecanica.Execucao.API/OficinaMecanica.Execucao.API.csproj

COPY src/ src/
RUN dotnet publish src/OficinaMecanica.Execucao.API/OficinaMecanica.Execucao.API.csproj -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
# Metricas OTLP em temporalidade delta: a New Relic zera os contadores quando recebe cumulativo.
ENV OTEL_EXPORTER_OTLP_METRICS_TEMPORALITY_PREFERENCE=delta
# A porta 8080 vem do ASPNETCORE_HTTP_PORTS da imagem base (aspnet:8.0); redefinir ASPNETCORE_URLS aqui geraria o aviso
# "Overriding HTTP_PORTS" a cada boot.
# Usuario nao-root: a imagem base ja traz o usuario app (UID em $APP_UID).
USER $APP_UID
ENTRYPOINT ["dotnet", "OficinaMecanica.Execucao.API.dll"]
