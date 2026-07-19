FROM mcr.microsoft.com/dotnet/sdk:10.0.301-alpine3.24@sha256:fc785a84b314fce6b7adb29ecc18542d7416f2e05c03ec1ebdb222eadaeafa50 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props Hospital.slnx ./
COPY backend/Hospital.Core/Hospital.Core.csproj backend/Hospital.Core/packages.lock.json backend/Hospital.Core/
COPY backend/Hospital.Infrastructure/Hospital.Infrastructure.csproj backend/Hospital.Infrastructure/packages.lock.json backend/Hospital.Infrastructure/
COPY backend/Hospital.Api/Hospital.Api.csproj backend/Hospital.Api/packages.lock.json backend/Hospital.Api/

RUN dotnet restore backend/Hospital.Api/Hospital.Api.csproj --locked-mode

COPY backend/ backend/

RUN dotnet publish backend/Hospital.Api/Hospital.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.10-alpine3.24@sha256:eb7c0c9ef04479bfff191036f6b8959a7d6bac983bd7160c6b8b84b20d3ad0e7 AS final
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080

COPY --from=build /app/publish .

USER $APP_UID

ENTRYPOINT ["dotnet", "Hospital.Api.dll"]
