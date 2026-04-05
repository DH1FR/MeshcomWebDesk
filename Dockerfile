# ── Build stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY MeshcomWebDesk/MeshcomWebDesk.csproj MeshcomWebDesk/
COPY MeshcomWebClient.Client/MeshcomWebClient.Client.csproj MeshcomWebClient.Client/
RUN dotnet restore MeshcomWebDesk/MeshcomWebDesk.csproj

COPY . .
RUN dotnet publish MeshcomWebDesk/MeshcomWebDesk.csproj \
    -c Release -r linux-x64 --self-contained true \
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true \
    -o /app/publish

# ── Runtime stage ─────────────────────────────────────────────────────────────
FROM debian:bookworm-slim AS runtime
WORKDIR /app

# libicu is required by .NET globalization
RUN apt-get update && apt-get install -y --no-install-recommends libicu-dev && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Default log directory (mount a volume to persist logs)
RUN mkdir -p /app/logs

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:5162
ENV Meshcom__LogPath=/app/logs

EXPOSE 5162
EXPOSE 1799/udp

ENTRYPOINT ["./MeshcomWebDesk"]
