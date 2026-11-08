# Monta o servidor (Linux) e o programa dos computadores (Windows) numa única imagem.
# Construída a partir da raiz do repositório: docker compose -f deploy/docker-compose.yml build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Monitor.slnx ./
COPY src/ src/
RUN dotnet publish src/Monitor.Server/Monitor.Server.csproj -c Release -o /out/server
RUN dotnet publish src/Monitor.Agent/Monitor.Agent.csproj -c Release -r win-x64 -o /out/agent

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out/server/ ./
COPY --from=build /out/agent/MonitorPortoconseg.exe ./agente/
COPY installer/ ./agente/

ENV DATA_DIR=/data \
    AGENT_DIR=/app/agente \
    ASPNETCORE_URLS=http://+:8080 \
    TZ=America/Sao_Paulo

# Pasta de dados pertence ao usuário sem privilégios da imagem (APP_UID).
RUN mkdir -p /data && chown -R ${APP_UID}:${APP_UID} /data
USER ${APP_UID}
VOLUME /data
EXPOSE 8080


ENTRYPOINT ["./Monitor.Server"]
