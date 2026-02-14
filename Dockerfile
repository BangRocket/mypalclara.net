# Multi-stage Dockerfile for MyPalClara.Web (primary deployment target)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first (for layer caching)
COPY MyPalClara.slnx ./
COPY src/Directory.Build.props src/
COPY src/MyPalClara.Core/MyPalClara.Core.csproj src/MyPalClara.Core/
COPY src/MyPalClara.Agent/MyPalClara.Agent.csproj src/MyPalClara.Agent/
COPY src/MyPalClara.Gateway/MyPalClara.Gateway.csproj src/MyPalClara.Gateway/
COPY src/MyPalClara.Memory/MyPalClara.Memory.csproj src/MyPalClara.Memory/
COPY src/MyPalClara.Voice/MyPalClara.Voice.csproj src/MyPalClara.Voice/
COPY src/MyPalClara.Skills/MyPalClara.Skills.csproj src/MyPalClara.Skills/
COPY src/MyPalClara.Web/MyPalClara.Web.csproj src/MyPalClara.Web/
COPY src/MyPalClara.Browser/MyPalClara.Browser.csproj src/MyPalClara.Browser/
COPY src/MyPalClara.Adapters.Cli/MyPalClara.Adapters.Cli.csproj src/MyPalClara.Adapters.Cli/
COPY src/MyPalClara.Adapters.Discord/MyPalClara.Adapters.Discord.csproj src/MyPalClara.Adapters.Discord/
COPY src/MyPalClara.Adapters.Ssh/MyPalClara.Adapters.Ssh.csproj src/MyPalClara.Adapters.Ssh/
COPY src/MyPalClara.Adapters.Telegram/MyPalClara.Adapters.Telegram.csproj src/MyPalClara.Adapters.Telegram/
COPY src/MyPalClara.Adapters.Slack/MyPalClara.Adapters.Slack.csproj src/MyPalClara.Adapters.Slack/
COPY src/MyPalClara.Adapters.WhatsApp/MyPalClara.Adapters.WhatsApp.csproj src/MyPalClara.Adapters.WhatsApp/
COPY src/MyPalClara.Adapters.Signal/MyPalClara.Adapters.Signal.csproj src/MyPalClara.Adapters.Signal/
COPY src/MyPalClara.Media/MyPalClara.Media.csproj src/MyPalClara.Media/
COPY src/MyPalClara.App.Terminal/MyPalClara.App.Terminal.csproj src/MyPalClara.App.Terminal/
COPY src/MyPalClara.Tools/MyPalClara.Tools.csproj src/MyPalClara.Tools/

# Restore
RUN dotnet restore src/MyPalClara.Web/MyPalClara.Web.csproj

# Copy everything and publish
COPY . .
RUN dotnet publish src/MyPalClara.Web/MyPalClara.Web.csproj -c Release -o /app --no-restore

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app .

# Create plugin and skills directories
RUN mkdir -p /app/plugins /app/skills

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "MyPalClara.Web.dll"]
