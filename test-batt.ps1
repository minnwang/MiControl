param(
    [int]$LoadSeconds = 300,
    [int]$CooldownSeconds = 30
)

$ErrorActionPreference = "Continue"
$state = Get-Content 'C:\ProgramData\MiControl\state.json' -Raw | ConvertFrom-Json
$burner = Join-Path $PSScriptRoot 'burn.ps1'

function Send-Ipc($obj) {
    $obj.token = $state.Token
    $c = New-Object System.Net.Sockets.TcpClient
    $c.Connect('127.0.0.1', $state.Port)
    $s = $c.GetStream()
    $w = New-Object System.IO.StreamWriter($s, [Text.Encoding]::UTF8); $w.AutoFlush = $true
    $r = New-Object System.IO.StreamReader($s, [Text.Encoding]::UTF8)
    $w.WriteLine(($obj | ConvertTo-Json -Compress))
    $resp = $r.ReadLine()
    $c.Close()
    return $resp
}

function Get-Status { Send-Ipc @{cmd='status'} | ConvertFrom-Json }

function Set-Perf([int]$mode) {
    Send-Ipc @{cmd='setPerf'; mode=$mode} | Out-Null
    Start-Sleep -Milliseconds 600
    return (Get-Status).perf
}

$cores = [Environment]::ProcessorCount
$counter = '\Processor Information(_Total)\% Processor Performance'
Write-Host "logical cores: $cores"
Write-Host "counter: $counter"

Write-Host "=== waiting for BATTERY power (up to 180s) ==="
$deadline = (Get-Date).AddSeconds(180)
$st = $null
while ((Get-Date) -lt $deadline) {
    $st = Get-Status
    if (-not $st.ac) { Write-Host "on battery, $($st.battery)%"; break }
    Start-Sleep -Seconds 2
}
if ($st.ac) { Write-Host "still on AC, abort"; exit 1 }

$original = (Get-Status).perf
Write-Host ("current mode = 0x{0:X2}" -f $original)

function Measure-Long([string]$label) {
    Write-Host "--- ${label}: cooldown ${CooldownSeconds}s"
    Start-Sleep -Seconds $CooldownSeconds

    $p = Start-Process -FilePath 'powershell.exe' -ArgumentList @(
        '-NoProfile','-ExecutionPolicy','Bypass','-File',"`"$burner`"",
        '-Threads',$cores,'-Seconds',$LoadSeconds
    ) -PassThru -WindowStyle Hidden

    Start-Sleep -Seconds 5
    $samples = @()
    $t0 = Get-Date
    while (((Get-Date) - $t0).TotalSeconds -lt ($LoadSeconds - 10)) {
        try {
            $v = (Get-Counter $counter -MaxSamples 1 -ErrorAction Stop).CounterSamples[0].CookedValue
            $samples += [double]$v
        } catch { }
        Start-Sleep -Seconds 5
    }
    $p | Wait-Process -Timeout ($LoadSeconds + 30) -ErrorAction SilentlyContinue
    if (-not $p.HasExited) { $p.Kill() }

    if ($samples.Count -lt 5) { return $null }
    $steady = $samples[[int]($samples.Count * 0.3)..($samples.Count - 1)]
    $avg = ($steady | Measure-Object -Average).Average
    $max = ($samples | Measure-Object -Maximum).Maximum
    $min = ($steady | Measure-Object -Minimum).Minimum
    Write-Host ("    n={0}  steady avg={1:N1}%  steady min={2:N1}%  peak={3:N1}%" -f $steady.Count, $avg, $min, $max)
    return [pscustomobject]@{ avg=$avg; min=$min; max=$max }
}

Write-Host "=== Performance (0x03) ==="
$rb = Set-Perf 0x03
Write-Host ("  readback=0x{0:X2}" -f $rb)
$mPerf = Measure-Long "Performance"

Write-Host "=== FullSpeed (0x04) ==="
$rb = Set-Perf 0x04
Write-Host ("  readback=0x{0:X2}" -f $rb)
$mFull = Measure-Long "FullSpeed"

Write-Host "=== restoring mode 0x{0:X2} ===" -f $original
Set-Perf $original | Out-Null

Write-Host ""
Write-Host "============== LONG TEST RESULT =============="
if ($mPerf) { Write-Host ("Performance : steady {0:N1}%  min {1:N1}%  peak {2:N1}%" -f $mPerf.avg, $mPerf.min, $mPerf.max) }
if ($mFull) { Write-Host ("FullSpeed   : steady {0:N1}%  min {1:N1}%  peak {2:N1}%" -f $mFull.avg, $mFull.min, $mFull.max) }
if ($mPerf -and $mFull) { Write-Host ("delta       : {0:N1} points" -f ($mFull.avg - $mPerf.avg)) }

