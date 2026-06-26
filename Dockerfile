# ── Build stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Ziel-RID wird vom Docker-Buildx je nach Plattform übergeben
ARG TARGETARCH
ARG APP_VERSION=""

COPY MeshcomWebDesk/MeshcomWebDesk.csproj MeshcomWebDesk/
RUN dotnet restore MeshcomWebDesk/MeshcomWebDesk.csproj \
    -r $( [ "$TARGETARCH" = "arm64" ] && echo "linux-arm64" || echo "linux-x64" )

COPY . .
RUN RID=$( [ "$TARGETARCH" = "arm64" ] && echo "linux-arm64" || echo "linux-x64" ) && \
    dotnet publish MeshcomWebDesk/MeshcomWebDesk.csproj \
    -c Release -r $RID --self-contained true \
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true \
    ${APP_VERSION:+-p:AssemblyVersion=${APP_VERSION}.0 -p:FileVersion=${APP_VERSION}.0 -p:InformationalVersion=${APP_VERSION}} \
    -o /app/publish

# ── Runtime stage ─────────────────────────────────────────────────────────────
FROM debian:bookworm-slim AS runtime
WORKDIR /app

# libicu is required by .NET globalization; tzdata for correct local time;
# ca-certificates is required for outgoing HTTPS connections (e.g. QRZ.com XML API)
RUN apt-get update \
    && apt-get install -y --no-install-recommends libicu-dev tzdata ca-certificates python3 \
    && ln -s /usr/bin/python3 /usr/local/bin/python \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Pre-create writable data directories so the app starts cleanly even when
# running as a non-root user or before a bind-mount is populated by Docker.
RUN mkdir -p /app/logs /app/data /app/keys /app/bot-scripts \
    && chmod 777 /app/logs /app/data /app/keys /app/bot-scripts

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:5162
ENV DOTNET_RUNNING_IN_CONTAINER=true
ENV Meshcom__LogPath=/app/logs
ENV Meshcom__DataPath=/app/data
ENV Meshcom__BotExternalCommandsPath=/app/bot-scripts
ENV TZ=Europe/Berlin

EXPOSE 5162
EXPOSE 1799/udp

ENTRYPOINT ["./MeshcomWebDesk"]
