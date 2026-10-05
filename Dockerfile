FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json Directory.Build.props .editorconfig stylecop.json ./
COPY app/ItmoBot.csproj app/packages.lock.json ./app/
RUN dotnet restore app/ItmoBot.csproj --locked-mode
COPY app/ ./app/
RUN dotnet publish app/ItmoBot.csproj -c Release --no-restore -o /publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "ItmoBot.dll"]
