# Build stage
# Uses .NET 10 nightly images since .NET 10 is in preview.
# Update to mcr.microsoft.com/dotnet/sdk:10.0 and aspnet:10.0 when .NET 10 GA releases.
FROM mcr.microsoft.com/dotnet/nightly/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files for restore (layer caching)
COPY MyPalClara.slnx .
COPY src/MyPalClara.Api/MyPalClara.Api.csproj src/MyPalClara.Api/
COPY src/MyPalClara.Core/MyPalClara.Core.csproj src/MyPalClara.Core/
COPY src/MyPalClara.Data/MyPalClara.Data.csproj src/MyPalClara.Data/
COPY src/MyPalClara.Llm/MyPalClara.Llm.csproj src/MyPalClara.Llm/
COPY src/MyPalClara.Memory/MyPalClara.Memory.csproj src/MyPalClara.Memory/
COPY tests/MyPalClara.Api.Tests/MyPalClara.Api.Tests.csproj tests/MyPalClara.Api.Tests/
COPY tests/MyPalClara.Core.Tests/MyPalClara.Core.Tests.csproj tests/MyPalClara.Core.Tests/
COPY tests/MyPalClara.Data.Tests/MyPalClara.Data.Tests.csproj tests/MyPalClara.Data.Tests/
COPY tests/MyPalClara.Memory.Tests/MyPalClara.Memory.Tests.csproj tests/MyPalClara.Memory.Tests/

RUN dotnet restore MyPalClara.slnx

# Copy all source and build
COPY . .
RUN dotnet publish src/MyPalClara.Api/MyPalClara.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/nightly/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# API port and WebSocket port
EXPOSE 18789
EXPOSE 18790

ENV ASPNETCORE_ENVIRONMENT=Production
ENV CLARA_GATEWAY_PORT=18789
ENV CLARA_GATEWAY_API_PORT=18790

ENTRYPOINT ["dotnet", "MyPalClara.Api.dll"]
