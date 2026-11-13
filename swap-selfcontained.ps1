param(
    [string]$Stage = "$env:TEMP\micontrol-pub",
    [string]$Target = "$env:LOCALAPPDATA\MiControl"
)

$ErrorActionPreference = "Continue"
Start-Transcript -Path "$env:TEMP\micontrol-swap.log" -Force | Out-Null

Write-Host "=== stop ==="
& sc.exe stop MiControlService | Out-Null
Start-Sleep -Seconds 2
Get-Process MiControlService, MiControlTray -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
Start-Sleep -Seconds 2

Write-Host "=== replace tray ==="
Remove-Item "$Target\tray\*" -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item "$Stage\tray\*" "$Target\tray" -Recurse -Force
Get-ChildItem "$Target\tray" | Select-Object Name, Length | Format-Table -AutoSize | Out-String | Write-Host

Write-Host "=== replace service ==="
Remove-Item "$Target\service\*" -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item "$Stage\service\*" "$Target\service" -Recurse -Force
Get-ChildItem "$Target\service" | Select-Object Name, Length | Format-Table -AutoSize | Out-String | Write-Host

Write-Host "=== start service ==="
& sc.exe start MiControlService
Start-Sleep -Seconds 6
& sc.exe query MiControlService

Write-Host "=== start tray ==="
Start-Process -FilePath "$Target\tray\MiControlTray.exe"
Start-Sleep -Seconds 3
Get-Process MiControlTray -ErrorAction SilentlyContinue | Select-Object Id, ProcessName

Stop-Transcript | Out-Null
