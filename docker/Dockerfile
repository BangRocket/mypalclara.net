FROM mcr.microsoft.com/dotnet/nightly/sdk:10.0 AS build
WORKDIR /src

COPY Clara.sln .
COPY src/Clara.Core/Clara.Core.csproj src/Clara.Core/
COPY src/Clara.Gateway/Clara.Gateway.csproj src/Clara.Gateway/
RUN dotnet restore Clara.sln

COPY . .
RUN dotnet publish src/Clara.Gateway/Clara.Gateway.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/nightly/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 18789 18790
ENV ASPNETCORE_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "Clara.Gateway.dll"]
