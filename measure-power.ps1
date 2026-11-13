$ErrorActionPreference = "Continue"
$out = "C:\Users\ming_\Documents\deepseek-harness\default-workspace\MiControl\power-timing.log"
Set-Content -Path $out -Value ("=== start " + (Get-Date -Format "HH:mm:ss.fff") + " ===") -Encoding UTF8
Add-Type -Language CSharp @"
using System;
using System.Runtime.InteropServices;
public static class PwrSt {
  [StructLayout(LayoutKind.Sequential)]
  public struct S { public byte AC; public byte Flag; public byte Pct; public byte Saver; public int Life; public int Full; }
  [DllImport("kernel32.dll")] public static extern bool GetSystemPowerStatus(out S s);
}
"@
$last = ""
$end = (Get-Date).AddSeconds(60)
while ((Get-Date) -lt $end) {
    $s = New-Object PwrSt+S
    [PwrSt]::GetSystemPowerStatus([ref]$s) | Out-Null
    $cur = "AC=$($s.AC) Pct=$($s.Pct) Flag=$($s.Flag)"
    if ($cur -ne $last) {
        Add-Content -Path $out -Value ("{0}  {1}" -f (Get-Date -Format "HH:mm:ss.fff"), $cur) -Encoding UTF8
        $last = $cur
    }
    Start-Sleep -Milliseconds 100
}
Add-Content -Path $out -Value "=== end ===" -Encoding UTF8
