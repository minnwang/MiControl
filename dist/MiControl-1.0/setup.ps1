param(
    [string]$InstallDir = "$env:LOCALAPPDATA\MiControl",
    [switch]$NoDesktopShortcut
)

$ErrorActionPreference = "Stop"

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    (New-Object Security.Principal.WindowsPrincipal $id).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Admin)) {
    Write-Host "Requesting administrator rights..."
    $args = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '"'
    Start-Process powershell.exe -Verb RunAs -ArgumentList $args
    return
}

$root = Split-Path -Parent $PSCommandPath
$svcName = "MiControlService"
$trayExe = Join-Path $InstallDir "tray\MiControlTray.exe"
$svcExe = Join-Path $InstallDir "service\MiControlService.exe"

Write-Host "=== stopping existing instance ==="
& sc.exe stop $svcName 2>$null | Out-Null
Start-Sleep -Seconds 2
Get-Process MiControlService, MiControlTray -ErrorAction SilentlyContinue | ForEach-Object {
    try { $_.Kill() } catch { }
}
Start-Sleep -Seconds 1
& sc.exe delete $svcName 2>$null | Out-Null
Start-Sleep -Seconds 1

Write-Host "=== copying files to $InstallDir ==="
New-Item -ItemType Directory -Path "$InstallDir\tray", "$InstallDir\service" -Force | Out-Null
Copy-Item "$root\tray\*" "$InstallDir\tray" -Recurse -Force
Copy-Item "$root\service\*" "$InstallDir\service" -Recurse -Force
Copy-Item "$root\README.md" $InstallDir -Force -ErrorAction SilentlyContinue

Write-Host "=== creating service ==="
New-Service -Name $svcName `
    -BinaryPathName "`"$svcExe`"" `
    -DisplayName "MiControl Service" `
    -Description "Battery charge limit and performance mode control (Xiaomi MIFS ACPI-WMI)." `
    -StartupType Automatic | Out-Null
& sc.exe failure $svcName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null

Write-Host "=== starting service ==="
& sc.exe start $svcName | Out-Null
Start-Sleep -Seconds 4

Write-Host "=== autostart (tray) ==="
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
New-ItemProperty -Path $runKey -Name "MiControlTray" -Value ('"' + $trayExe + '"') -PropertyType String -Force | Out-Null

Write-Host "=== shortcuts ==="
$shell = New-Object -ComObject WScript.Shell
$startMenu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\MiControl.lnk"
$sc = $shell.CreateShortcut($startMenu)
$sc.TargetPath = $trayExe
$sc.WorkingDirectory = Split-Path $trayExe
$sc.IconLocation = "$trayExe,0"
$sc.Description = "MiControl"
$sc.Save()

if (-not $NoDesktopShortcut) {
    $desktop = Join-Path ([Environment]::GetFolderPath("Desktop")) "MiControl.lnk"
    $dc = $shell.CreateShortcut($desktop)
    $dc.TargetPath = $trayExe
    $dc.WorkingDirectory = Split-Path $trayExe
    $dc.IconLocation = "$trayExe,0"
    $dc.Description = "MiControl"
    $dc.Save()
}

Write-Host "=== starting tray ==="
Start-Process -FilePath $trayExe

Start-Sleep -Seconds 2
Write-Host ""
Write-Host "=== installed ==="
& sc.exe query $svcName | Select-String "STATE"
Get-Process MiControlTray -ErrorAction SilentlyContinue | Select-Object Id, ProcessName
Write-Host "tray : $trayExe"
Write-Host "svc  : $svcExe"
Write-Host "config: $env:ProgramData\MiControl\state.json"
