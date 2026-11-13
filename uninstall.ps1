param(
    [switch]$KeepServiceFiles
)

$ErrorActionPreference = "Continue"
$name = "MiControlService"

Write-Host "=== stopping service ==="
& sc.exe stop $name | Out-Null
Start-Sleep -Seconds 2
& sc.exe delete $name | Out-Null

Write-Host "=== stopping tray ==="
Get-Process -Name "MiControlTray" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
    Where-Object { $_.CommandLine -like '*MiControlTray*' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

Write-Host "=== removing autostart ==="
Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "MiControlTray" -ErrorAction SilentlyContinue

Write-Host "=== restoring hardware defaults (charge 100%, perf Smart) ==="
$exe = Join-Path $PSScriptRoot "MiControl.Service\bin\Release\net8.0\win-x64\MiControlService.exe"
if (Test-Path $exe) {
    & $exe --reset-defaults
}

if (-not $KeepServiceFiles) {
    Write-Host "=== removing program data ==="
    Remove-Item "C:\ProgramData\MiControl" -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "done"
