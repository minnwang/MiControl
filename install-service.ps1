param(
    [string]$ServiceExe = "C:\Users\ming_\Documents\deepseek-harness\default-workspace\MiControl\MiControl.Service\bin\Release\net8.0\win-x64\MiControlService.exe"
)

$ErrorActionPreference = "Continue"
Start-Transcript -Path (Join-Path $PSScriptRoot "install-service.log") -Force | Out-Null

$name = "MiControlService"
Write-Host "exe: $ServiceExe"
Write-Host "exists: $(Test-Path $ServiceExe)"

Write-Host "=== stop and remove old definition ==="
& sc.exe stop $name | Out-Null
Start-Sleep -Seconds 1
Get-Process -Name "MiControlService" -ErrorAction SilentlyContinue | ForEach-Object {
    try { $_.Kill() } catch { }
}
Start-Sleep -Seconds 1
& sc.exe delete $name | Out-Null
Start-Sleep -Seconds 2

Write-Host "=== create service ==="
try {
    New-Service -Name $name `
        -BinaryPathName "`"$ServiceExe`"" `
        -DisplayName "MiControl Service" `
        -Description "Battery charge limit and performance mode control (MIFS ACPI-WMI)." `
        -StartupType Automatic `
        -ErrorAction Stop
    Write-Host "created"
} catch {
    Write-Host "create failed: $($_.Exception.Message)"
}

& sc.exe failure $name reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null
& sc.exe qc $name

Write-Host "=== start ==="
& sc.exe start $name
Start-Sleep -Seconds 5
& sc.exe query $name
Get-Process -Name "MiControlService" -ErrorAction SilentlyContinue | Select-Object Id, ProcessName

Stop-Transcript | Out-Null
