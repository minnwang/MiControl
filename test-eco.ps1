param(
    [int]$LoadSeconds = 120,
    [int]$CooldownSeconds = 20
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

Write-Host "=== waiting for BATTERY (up to 180s) ==="
$deadline = (Get-Date).AddSeconds(180)
$st = $null
while ((Get-Date) -lt $deadline) {
    $st = Get-Status
    if (-not $st.ac) { Write-Host "on battery, $($st.battery)%"; break }
    Start-Sleep -Seconds 2
}
if ($st.ac) { Write-Host "still on AC, abort"; exit 1 }

$original = (Get-Status).perf
Write-Host ("current mode = 0x{0:X2}  cores=$cores" -f $original)

function Measure-Mode([string]$label) {
    Write-Host "--- ${label}: cooldown ${CooldownSeconds}s"
    Start-Sleep -Seconds $CooldownSeconds

    $p = Start-Process -FilePath 'powershell.exe' -ArgumentList @(
        '-NoProfile','-ExecutionPolicy','Bypass','-File',"`"$burner`"",
        '-Threads',$cores,'-Seconds',$LoadSeconds
    ) -PassThru -WindowStyle Hidden

    Start-Sleep -Seconds 4
    $samples = @()
    $t0 = Get-Date
    while (((Get-Date) - $t0).TotalSeconds -lt ($LoadSeconds - 8)) {
        try {
            $v = (Get-Counter $counter -MaxSamples 1 -ErrorAction Stop).CounterSamples[0].CookedValue
            $samples += [double]$v
        } catch { }
        Start-Sleep -Seconds 4
    }
    $p | Wait-Process -Timeout ($LoadSeconds + 30) -ErrorAction SilentlyContinue
    if (-not $p.HasExited) { $p.Kill() }

    if ($samples.Count -lt 3) { return $null }
    $steady = $samples[[int]($samples.Count * 0.25)..($samples.Count - 1)]
    $avg = ($steady | Measure-Object -Average).Average
    $max = ($samples | Measure-Object -Maximum).Maximum
    Write-Host ("    n={0}  steady avg={1:N1}%  peak={2:N1}%" -f $steady.Count, $avg, $max)
    return [pscustomobject]@{ avg=$avg; max=$max }
}

$results = [ordered]@{}

foreach ($m in @(
    @{ name='Quiet';       code=0x02 },
    @{ name='ECO';         code=0x0A },
    @{ name='Performance'; code=0x03 }
)) {
    Write-Host ("=== {0} (0x{1:X2}) ===" -f $m.name, $m.code)
    $rb = Set-Perf $m.code
    Write-Host ("  readback=0x{0:X2}" -f $rb)
    $results[$m.name] = Measure-Mode $m.name
}

Write-Host ("=== restoring 0x{0:X2} ===" -f $original)
Set-Perf $original | Out-Null

Write-Host ""
Write-Host "============== BATTERY MODE TEST =============="
foreach ($k in $results.Keys) {
    $v = $results[$k]
    if ($v) { Write-Host ("{0,-12}: steady {1,6:N1}%  peak {2,6:N1}%" -f $k, $v.avg, $v.max) }
}
