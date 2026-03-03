# Build stage
# Uses .NET 10 nightly images since .NET 10 is in preview.
# Update to mcr.microsoft.com/dotnet/sdk:10.0 and aspnet:10.0 when .NET 10 GA releases.
FROM mcr.microsoft.com/dotnet/nightly/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files for restore (layer caching)
COPY Clara.slnx .
COPY src/Clara.Core/Clara.Core.csproj src/Clara.Core/
COPY src/Clara.Gateway/Clara.Gateway.csproj src/Clara.Gateway/
COPY src/Clara.Adapters.Discord/Clara.Adapters.Discord.csproj src/Clara.Adapters.Discord/
COPY src/Clara.Adapters.Teams/Clara.Adapters.Teams.csproj src/Clara.Adapters.Teams/
COPY src/Clara.Adapters.Cli/Clara.Adapters.Cli.csproj src/Clara.Adapters.Cli/
COPY tests/Clara.Core.Tests/Clara.Core.Tests.csproj tests/Clara.Core.Tests/
COPY tests/Clara.Gateway.Tests/Clara.Gateway.Tests.csproj tests/Clara.Gateway.Tests/

RUN dotnet restore Clara.slnx

# Copy all source and build
COPY . .
RUN dotnet publish src/Clara.Gateway/Clara.Gateway.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/nightly/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .
COPY workspace/ /app/workspace/

# Gateway port
EXPOSE 18789

ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "Clara.Gateway.dll"]
