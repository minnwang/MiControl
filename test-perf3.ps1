param(
    [int]$LoadSeconds = 15,
    [int]$CooldownSeconds = 20,
    [int]$Threads = 6
)

$ErrorActionPreference = "Continue"
$state = Get-Content 'C:\ProgramData\MiControl\state.json' -Raw | ConvertFrom-Json

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

function Get-Perf { (Send-Ipc @{cmd='status'} | ConvertFrom-Json).perf }

function Set-Perf([int]$mode) {
    $r = Send-Ipc @{cmd='setPerf'; mode=$mode} | ConvertFrom-Json
    Start-Sleep -Milliseconds 500
    $actual = Get-Perf
    return [pscustomobject]@{ ok=$r.ok; actual=$actual }
}

$counter = '\Processor Information(_Total)\% Processor Performance'
try { Get-Counter $counter -MaxSamples 1 -ErrorAction Stop | Out-Null }
catch { $counter = '\Processor Information(_Total)\% of Maximum Frequency' }
Write-Host "counter: $counter"

function Measure-Mode([string]$label) {
    Write-Host "--- ${label}: cooling down ${CooldownSeconds}s"
    Start-Sleep -Seconds $CooldownSeconds

    $jobs = @()
    for ($i = 0; $i -lt $Threads; $i++) {
        $jobs += Start-Job -ScriptBlock {
            param($seconds)
            $end = (Get-Date).AddSeconds($seconds)
            $x = 0.0
            while ((Get-Date) -lt $end) { $x += [Math]::Sqrt($x + 1.0) }
        } -ArgumentList $LoadSeconds
    }

    Start-Sleep -Seconds 2
    $samples = @()
    for ($i = 0; $i -lt ($LoadSeconds - 3); $i++) {
        try {
            $v = (Get-Counter $counter -MaxSamples 1 -ErrorAction Stop).CounterSamples[0].CookedValue
            $samples += [double]$v
        } catch { }
        Start-Sleep -Seconds 1
    }
    $jobs | Wait-Job -Timeout ($LoadSeconds + 15) | Out-Null
    $jobs | Remove-Job -Force -ErrorAction SilentlyContinue

    if ($samples.Count -eq 0) { return $null }
    $avg = ($samples | Measure-Object -Average).Average
    $max = ($samples | Measure-Object -Maximum).Maximum
    Write-Host ("    samples={0}  avg={1:N1}%  max={2:N1}%" -f $samples.Count, $avg, $max)
    return [pscustomobject]@{ avg=$avg; max=$max; count=$samples.Count }
}

$original = Get-Perf
Write-Host ("current mode = 0x{0:X2}" -f $original)

Write-Host "=== waiting for AC power (up to 120s) ==="
$deadline = (Get-Date).AddSeconds(120)
while ((Get-Date) -lt $deadline) {
    $st = (Send-Ipc @{cmd='status'}) | ConvertFrom-Json
    if ($st.ac) { Write-Host "AC connected, battery=$($st.battery)%"; break }
    Start-Sleep -Seconds 2
}
if (-not $st.ac) { Write-Host "AC not connected, aborting"; exit 1 }

Write-Host "=== switching to Smart (0x09) ==="
$r1 = Set-Perf 0x09
Write-Host ("  readback=0x{0:X2}" -f $r1.actual)
$m1 = Measure-Mode "Smart"

Write-Host "=== switching to Performance (0x03) ==="
$r2 = Set-Perf 0x03
Write-Host ("  readback=0x{0:X2}" -f $r2.actual)
$m2 = Measure-Mode "Performance"

Write-Host "=== switching to FullSpeed (0x04) ==="
$r3 = Set-Perf 0x04
Write-Host ("  readback=0x{0:X2}" -f $r3.actual)
$m3 = Measure-Mode "FullSpeed"

Write-Host "=== restoring original mode ==="
Set-Perf $original | Out-Null

Write-Host ""
Write-Host "================ RESULT ================"
foreach ($pair in @(@("Smart",$m1), @("Performance",$m2), @("FullSpeed",$m3))) {
    if ($pair[1]) { Write-Host ("{0,-12}: avg {1,6:N1}%  max {2,6:N1}%" -f $pair[0], $pair[1].avg, $pair[1].max) }
}
if ($m2 -and $m3) {
    Write-Host ("FullSpeed - Performance = {0:N1} points" -f ($m3.avg - $m2.avg))
}



