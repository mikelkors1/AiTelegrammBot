$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
& dotnet run --project tools/ItmoBot.Tools.csproj -- @args
exit $LASTEXITCODE
