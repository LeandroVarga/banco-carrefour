FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
RUN dotnet publish src/Consolidation/Consolidation.Worker/Consolidation.Worker.csproj \
    --configuration Release \
    --output /app/publish \
    --no-self-contained

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Não precisa instalar CA/trust store (não termina TLS nem chama a borda) -
# ao contrário das APIs (ver docker/api-entrypoint.sh), pode trocar para o
# usuário não privilegiado direto no Dockerfile, sem entrypoint próprio.
USER $APP_UID

ENTRYPOINT ["dotnet", "Consolidation.Worker.dll"]
