param(
    [string]$Targets = "C:\ProgramData\MI|C:\Program Files (x86)\Timi Personal Computing"
)

$ErrorActionPreference = "Continue"
Start-Transcript -Path "$env:TEMP\micontrol-cleanup.log" -Force | Out-Null

foreach ($p in $Targets.Split('|')) {
    if (Test-Path $p) {
        $size = (Get-ChildItem $p -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
        Write-Host ("removing {0}  ({1:N0} MB)" -f $p, ($size / 1MB))
        Remove-Item $p -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host ("  exists now: {0}" -f (Test-Path $p))
    } else {
        Write-Host "not found: $p"
    }
}

Write-Host "=== remaining ==="
foreach ($p in "C:\ProgramData\MI", "C:\Program Files (x86)\Timi Personal Computing", "C:\Program Files\MI") {
    if (Test-Path $p) {
        $s = (Get-ChildItem $p -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
        Write-Host ("{0}  {1:N0} MB" -f $p, ($s / 1MB))
    } else {
        Write-Host "$p  gone"
    }
}

Stop-Transcript | Out-Null
