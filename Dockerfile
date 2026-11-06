FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json Directory.Build.props .editorconfig stylecop.json ./
COPY agent/ItmoBot.Agent.csproj agent/packages.lock.json ./agent/
COPY app/ItmoBot.csproj app/packages.lock.json ./app/
COPY mcp/ItmoBot.Mcp.csproj mcp/packages.lock.json ./mcp/
RUN dotnet restore app/ItmoBot.csproj --locked-mode
COPY agent/ ./agent/
COPY app/ ./app/
COPY mcp/ ./mcp/
RUN dotnet publish app/ItmoBot.csproj -c Release --no-restore -o /publish
RUN dotnet publish mcp/ItmoBot.Mcp.csproj -c Release --no-restore -o /publish/mcp

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV MCP_SERVER_PATH=/app/mcp/ItmoBot.Mcp.dll
COPY --from=build /publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "ItmoBot.dll"]
