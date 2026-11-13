param(
    [string]$InstallDir = "$env:LOCALAPPDATA\MiControl",
    [switch]$Purge
)

$ErrorActionPreference = "Continue"

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object Security.Principal.WindowsPrincipal $id).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Admin)) {
    $args = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '"'
    Start-Process powershell.exe -Verb RunAs -ArgumentList $args
    return
}

$svcName = "MiControlService"
$svcExe = Join-Path $InstallDir "service\MiControlService.exe"

Write-Host "=== restoring hardware defaults (charge 100%, performance Smart) ==="
if (Test-Path $svcExe) {
    & $svcExe --reset-defaults
}

Write-Host "=== stopping service ==="
& sc.exe stop $svcName | Out-Null
Start-Sleep -Seconds 2
Get-Process MiControlService, MiControlTray -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
Start-Sleep -Seconds 1
& sc.exe delete $svcName | Out-Null

Write-Host "=== removing autostart and shortcuts ==="
Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "MiControlTray" -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\MiControl.lnk") -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path ([Environment]::GetFolderPath("Desktop")) "MiControl.lnk") -Force -ErrorAction SilentlyContinue

if ($Purge) {
    Write-Host "=== purging program files and settings ==="
    Remove-Item $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item "$env:ProgramData\MiControl" -Recurse -Force -ErrorAction SilentlyContinue
} else {
    Write-Host "(program files kept; run with -Purge to remove them as well)"
}

Write-Host "done"
