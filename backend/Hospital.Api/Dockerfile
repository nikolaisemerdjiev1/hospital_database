FROM node:24.21.0-alpine3.24@sha256:be80f76cf40ec8e42b9bec49f60a55e0660f30af58d3e5a25530785b30ea67e2 AS frontend
WORKDIR /frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
ARG VITE_AUTH0_DOMAIN
ARG VITE_AUTH0_CLIENT_ID
ARG VITE_AUTH0_AUDIENCE
# Only public client settings are build arguments. The public demo value is a secret
# input to avoid retaining it in image metadata; it is intentionally visible in the UI.
RUN --mount=type=secret,id=demo_password,env=VITE_DEMO_PASSWORD \
    test -n "$VITE_AUTH0_DOMAIN" && test -n "$VITE_AUTH0_CLIENT_ID" && \
    test -n "$VITE_AUTH0_AUDIENCE" && npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0.301-alpine3.24@sha256:fc785a84b314fce6b7adb29ecc18542d7416f2e05c03ec1ebdb222eadaeafa50 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props Hospital.slnx ./
COPY backend/Hospital.Core/Hospital.Core.csproj backend/Hospital.Core/packages.lock.json backend/Hospital.Core/
COPY backend/Hospital.Infrastructure/Hospital.Infrastructure.csproj backend/Hospital.Infrastructure/packages.lock.json backend/Hospital.Infrastructure/
COPY backend/Hospital.Api/Hospital.Api.csproj backend/Hospital.Api/packages.lock.json backend/Hospital.Api/

RUN dotnet restore backend/Hospital.Api/Hospital.Api.csproj --locked-mode

COPY backend/ backend/
COPY scripts/production/grant-runtime.sql scripts/production/grant-runtime.sql

RUN dotnet publish backend/Hospital.Api/Hospital.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12-alpine3.24@sha256:6bb0fab0ef31f44f710a668c39c2263ae810f5adf868afa34cbd86815912c7fe AS final
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080

COPY --from=build /app/publish .
COPY --from=frontend /frontend/dist ./wwwroot

USER $APP_UID

ENTRYPOINT ["dotnet", "Hospital.Api.dll"]
