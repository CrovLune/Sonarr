# Frontend build stage (platform-independent output, build on native arch only)
FROM node:22-bookworm-slim AS frontend
WORKDIR /build

COPY package.json yarn.lock tsconfig.json ./
RUN yarn install --frozen-lockfile --production=false

COPY frontend/ frontend/
RUN yarn build --env production

# Backend build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json ./
COPY Logo/ Logo/
COPY src/ src/

RUN dotnet restore src/NzbDrone.Console/Sonarr.Console.csproj -p:NuGetAudit=false && \
    dotnet restore src/NzbDrone.Mono/Sonarr.Mono.csproj -p:NuGetAudit=false
RUN dotnet publish src/NzbDrone.Console/Sonarr.Console.csproj \
    -c Release \
    -f net10.0 \
    -o /app \
    --no-restore \
    -p:TreatWarningsAsErrors=false && \
    dotnet publish src/NzbDrone.Mono/Sonarr.Mono.csproj \
    -c Release \
    -f net10.0 \
    -o /app \
    --no-restore \
    -p:TreatWarningsAsErrors=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble
WORKDIR /app

COPY --from=build /app .
COPY --from=frontend /build/_output/UI ./UI

RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

RUN mkdir -p /config && touch /.dockerenv

EXPOSE 8989

ENTRYPOINT ["./Sonarr", "-data=/config"]
