param(
    [string]$ServiceExe = "C:\Users\ming_\Documents\deepseek-harness\default-workspace\MiControl\MiControl.Service\bin\Release\net8.0\win-x64\MiControlService.exe",
    [string]$Log = "C:\Users\ming_\Documents\deepseek-harness\default-workspace\MiControl\service-run.log"
)

$ErrorActionPreference = "Continue"

try { New-Item -ItemType Directory -Path "C:\ProgramData\MiControl" -Force | Out-Null } catch { }

"=== start $(Get-Date -Format o) ===" | Set-Content -Path $Log -Encoding UTF8
"exe: $ServiceExe" | Add-Content $Log
"exists: $(Test-Path $ServiceExe)" | Add-Content $Log

if (-not (Test-Path $ServiceExe)) { "MISSING EXE" | Add-Content $Log; exit 2 }

$p = Start-Process -FilePath $ServiceExe -NoNewWindow -PassThru `
    -RedirectStandardOutput ($Log + ".out") -RedirectStandardError ($Log + ".err")
"started pid=$($p.Id)" | Add-Content $Log
$p.WaitForExit(3000) | Out-Null
if ($p.HasExited) {
    "exited early code=$($p.ExitCode)" | Add-Content $Log
} else {
    "running ok pid=$($p.Id)" | Add-Content $Log
}
