param(
    [string]$Root = "C:\Users\ming_\Documents\deepseek-harness\default-workspace\MiControl",
    [string]$Dotnet = "$env:USERPROFILE\.dotnet-sdk8\dotnet.exe"
)

$ErrorActionPreference = "Continue"
Start-Transcript -Path (Join-Path $Root "deploy.log") -Force | Out-Null

$name = "MiControlService"
$svcExe = Join-Path $Root "MiControl.Service\bin\Release\net8.0\win-x64\MiControlService.exe"

Write-Host "=== stop service ==="
& sc.exe stop $name | Out-Null
Start-Sleep -Seconds 2
Get-Process -Name "MiControlService" -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
Start-Sleep -Seconds 2

Write-Host "=== stop tray (holds the dll lock) ==="
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like '*MiControlTray*' } |
    ForEach-Object { try { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue } catch { } }
Get-Process -Name "MiControlTray" -ErrorAction SilentlyContinue | ForEach-Object { try { Stop-Process -Id $_.Id -Force } catch { } }
Start-Sleep -Seconds 2

Write-Host "=== build ==="
& $Dotnet build (Join-Path $Root "MiControl.Service\MiControl.Service.csproj") -c Release --nologo
$svcBuild = $LASTEXITCODE
& $Dotnet build (Join-Path $Root "MiControl.Tray\MiControl.Tray.csproj") -c Release --nologo
$trayBuild = $LASTEXITCODE
Write-Host "service build=$svcBuild tray build=$trayBuild"

Write-Host "=== start service ==="
& sc.exe start $name
Start-Sleep -Seconds 5
& sc.exe query $name

Write-Host "=== keys.log ==="
$keys = Join-Path $env:ProgramData "MiControl\keys.log"
if (Test-Path $keys) { Get-Content $keys -Tail 20 }

Stop-Transcript | Out-Null
