param(
    [int]$Seconds = 600,
    [string]$OutFile = "C:\Users\ming_\Documents\deepseek-harness\default-workspace\MiControl\key-events.log"
)

$ErrorActionPreference = "Continue"

function Log([string]$m) {
    Add-Content -Path $OutFile -Value ("{0}  {1}" -f (Get-Date -Format "HH:mm:ss.fff"), $m) -Encoding UTF8
}

Set-Content -Path $OutFile -Value ("=== key listener start {0} ===" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss")) -Encoding UTF8

$classes = @("HID_EVENT20", "HID_EVENT21", "HID_EVENT22", "HID_EVENT23")
$watchers = @()

foreach ($name in $classes) {
    try {
        $query = "SELECT * FROM $name"
        $w = New-Object System.Management.ManagementEventWatcher -ArgumentList "root\wmi", $query
        $w.Options.Timeout = [TimeSpan]::FromSeconds(10)
        $cls = $name
        $handler = [System.Management.EventArrivedEventHandler] {
            param($s, $e)
            $who = $cls
            try {
                $d = $e.NewEvent["EventDetail"]
                $hex = ($d | ForEach-Object { $_.ToString("X2") }) -join " "
                Log ("EVENT {0}  [{1}]" -f $who, $hex)
            } catch {
                Log ("EVENT {0}  unreadable: {1}" -f $who, $_.Exception.Message)
            }
        }.GetNewClosure()
        $w.add_EventArrived($handler)
        $w.Start()
        $watchers += $w
        Log "subscribed: $name"
    } catch {
        Log ("subscribe failed {0}: {1}" -f $name, $_.Exception.Message)
    }
}

$deadline = (Get-Date).AddSeconds($Seconds)
while ((Get-Date) -lt $deadline) { Start-Sleep -Seconds 1 }

foreach ($w in $watchers) { try { $w.Stop() } catch { } }
Log "=== listener end ==="
