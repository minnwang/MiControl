$v = '"C:\Program Files\MiControl\tray\MiControlTray.exe"'
Set-ItemProperty -Path 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'MiControlTray' -Value $v
"set to: " + (Get-ItemProperty 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run').MiControlTray
