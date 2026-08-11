# Build context is the repo root (docker-compose.yml's `context: .`) --
# every project reference this Host needs lives under src/, so the whole
# tree is copied in before publish, not just this one project's folder.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/EventStore.Host.Postgres/EventStore.Host.Postgres.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "EventStore.Host.Postgres.dll"]
