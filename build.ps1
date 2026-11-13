param(
    [string]$Dotnet = "$env:USERPROFILE\.dotnet-sdk8\dotnet.exe",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"

if (-not (Test-Path $Dotnet)) { throw "dotnet not found at $Dotnet" }

Write-Host "=== restore & build ===" -ForegroundColor Cyan
& $Dotnet build "$root\MiControl.Service\MiControl.Service.csproj" -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "service build failed" }

& $Dotnet build "$root\MiControl.Tray\MiControl.Tray.csproj" -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "tray build failed" }

Write-Host "=== output ===" -ForegroundColor Cyan
Get-ChildItem "$root\MiControl.Service\bin\$Configuration" -Recurse -Filter MiControlService.exe | Select-Object FullName
Get-ChildItem "$root\MiControl.Tray\bin\$Configuration" -Recurse -Filter MiControlTray.exe | Select-Object FullName
