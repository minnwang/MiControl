$ErrorActionPreference = "Continue"
Start-Transcript -Path "$env:TEMP\micontrol-deploy.log" -Force | Out-Null
$target = "C:\Program Files\MiControl"
$stage  = "$env:TEMP\micontrol-pub"

Write-Host "=== stop ==="
& sc.exe stop MiControlService | Out-Null
Start-Sleep -Seconds 2
Get-Process MiControlService, MiControlTray -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch { } }
Start-Sleep -Seconds 2

Write-Host "=== copy ==="
Copy-Item "$stage\tray\MiControlTray.exe" "$target\tray\" -Force
Copy-Item "$stage\service\MiControlService.exe" "$target\service\" -Force
Get-ChildItem $target -Recurse -File | Select-Object FullName, Length | Format-Table -AutoSize | Out-String | Write-Host

Write-Host "=== start ==="
& sc.exe start MiControlService | Out-Null
Start-Sleep -Seconds 6
& sc.exe query MiControlService | Select-String "STATE"
Start-Process -FilePath "$target\tray\MiControlTray.exe"
Start-Sleep -Seconds 3
Get-Process MiControlTray -ErrorAction SilentlyContinue | Select-Object Id, ProcessName

Stop-Transcript | Out-Null
