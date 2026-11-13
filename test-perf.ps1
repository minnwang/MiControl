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

Write-Host "=== switching to Smart (0x09) ==="
$r1 = Set-Perf 0x09
Write-Host ("  requested ok={0}, readback=0x{1:X2}" -f $r1.ok, $r1.actual)
$m1 = Measure-Mode "Smart"

Write-Host "=== switching to FullSpeed (0x04) ==="
$r2 = Set-Perf 0x04
Write-Host ("  requested ok={0}, readback=0x{1:X2}" -f $r2.ok, $r2.actual)
$m2 = Measure-Mode "FullSpeed"

Write-Host "=== restoring original mode ==="
Set-Perf $original | Out-Null

Write-Host ""
Write-Host "================ RESULT ================"
if ($m1 -and $m2) {
    Write-Host ("Smart     : avg {0:N1}%  max {1:N1}%" -f $m1.avg, $m1.max)
    Write-Host ("FullSpeed : avg {0:N1}%  max {1:N1}%" -f $m2.avg, $m2.max)
    Write-Host ("delta     : {0:N1} percentage points" -f ($m2.avg - $m1.avg))
} else {
    Write-Host "measurement failed"
}

