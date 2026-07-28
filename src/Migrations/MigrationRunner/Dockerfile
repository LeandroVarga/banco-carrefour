FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY . .
RUN dotnet publish src/Migrations/MigrationRunner/MigrationRunner.csproj \
    --configuration Release \
    --output /app/publish \
    --no-self-contained

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Executável one-off (nunca um ECS Service de longa duração, nunca um dos
# 4 workloads de negócio) - não termina TLS nem chama a borda, pode trocar
# para o usuário não privilegiado direto no Dockerfile, sem entrypoint
# próprio (mesmo padrão de Ledger.OutboxPublisher/Consolidation.Worker).
USER $APP_UID

ENTRYPOINT ["dotnet", "MigrationRunner.dll"]
