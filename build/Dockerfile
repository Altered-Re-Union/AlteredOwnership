# syntax=docker/dockerfile:1
#
# Build context is the repo root. Two targets:
#   docker build -f build/Dockerfile -t altered-ownership .
#   docker build -f build/Dockerfile --target migrations -t altered-ownership-migrations .
#
# `app` is the standalone AlteredOwnership.Server. It expects:
#   - ConnectionStrings__ownershipdb : Postgres connection string
#   - ConnectionStrings__cache       : Redis connection string
#   - Authentication__Keycloak__*    : OIDC settings (see appsettings.json)
#
# `migrations` is a one-shot container that applies EF Core migrations and exits.
# The compiled-in connection string comes from OwnershipDbContextDesignFactory
# (localhost dev default) and is meant to be overridden at runtime:
#   docker run --rm altered-ownership-migrations --connection "Host=postgres;..."
# Aspire is only used for local dev (AlteredOwnership.AppHost); it is not part
# of either image.

ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS restore
WORKDIR /src
COPY AlteredOwnership.Server/AlteredOwnership.Server.csproj AlteredOwnership.Server/
RUN dotnet restore AlteredOwnership.Server/AlteredOwnership.Server.csproj
COPY AlteredOwnership.Server/ AlteredOwnership.Server/

FROM restore AS publish
RUN dotnet publish AlteredOwnership.Server/AlteredOwnership.Server.csproj \
      -c Release \
      -o /app/publish \
      /p:UseAppHost=false

FROM restore AS migrations-build
ENV PATH="${PATH}:/root/.dotnet/tools"
RUN dotnet tool install --global dotnet-ef --version 10.* \
 && dotnet ef migrations bundle \
      --project AlteredOwnership.Server/AlteredOwnership.Server.csproj \
      --configuration Release \
      --output /app/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS migrations
WORKDIR /app
COPY --from=migrations-build /app/efbundle .
ENTRYPOINT ["./efbundle"]

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS app
WORKDIR /app
COPY --from=publish /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "AlteredOwnership.Server.dll"]
