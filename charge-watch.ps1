param(
    [int]$Seconds = 300,
    [string]$OutFile = "C:\Users\ming_\Documents\deepseek-harness\default-workspace\MiControl\charge-watch.log"
)

$ErrorActionPreference = "Continue"

function Log([string]$m) {
    Add-Content -Path $OutFile -Value ("{0}  {1}" -f (Get-Date -Format "HH:mm:ss.fff"), $m) -Encoding UTF8
}

Set-Content -Path $OutFile -Value ("=== charge watch {0} ===" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss")) -Encoding UTF8

$inst = $null
try {
    $inst = Get-WmiObject -Namespace "root\wmi" -Class "MICommonInterface" -ErrorAction Stop | Select-Object -First 1
    Log "MIFS connected"
} catch {
    Log ("MIFS connect failed: " + $_.Exception.Message); exit 1
}

function Get-ChargeCode {
    $buf = [byte[]]::new(32); $buf[1] = 0xFA; $buf[3] = 0x10; $buf[4] = 0x02
    try {
        $mp = $inst.GetMethodParameters("MiInterface"); $mp["InData"] = [byte[]]$buf
        $r = $inst.InvokeMethod("MiInterface", $mp, $null)
        $o = [byte[]]$r["OutData"]
        if ($o[1] -eq 0x80) { return [int]$o[6] }
    } catch { }
    return $null
}

$last = Get-ChargeCode
Log ("initial code = {0}" -f $(if ($null -ne $last) { "0x{0:X2}" -f $last } else { "n/a" }))

$deadline = (Get-Date).AddSeconds($Seconds)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 700
    $cur = Get-ChargeCode
    if ($null -ne $cur -and $cur -ne $last) {
        Log ("CHANGED -> 0x{0:X2} ({1})" -f $cur, $cur)
        $last = $cur
    }
}

Log "=== end ==="
