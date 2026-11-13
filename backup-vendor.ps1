param(
    [string]$Root = "C:\Users\ming_\Documents\deepseek-harness\default-workspace\MiControl",
    [string]$XiaomiRoot = "C:\Program Files\MI\XiaomiPCManager"
)

$ErrorActionPreference = "Continue"
Start-Transcript -Path (Join-Path $Root "backup.log") -Force | Out-Null

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$dest = Join-Path $Root "backup\vendor-$stamp"
New-Item -ItemType Directory -Path $dest -Force | Out-Null
Write-Host "backup dest: $dest"

# 1) EC I/O driver (needed later for keyboard-backlight / EC access)
$ec = Join-Path $XiaomiRoot "6.0.2.30\MiSmartShare\Drivers\EcIo"
if (Test-Path $ec) {
    Copy-Item $ec (Join-Path $dest "EcIo") -Recurse -Force
    Write-Host "copied EcIo driver folder"
} else {
    Write-Host "EcIo folder not found at $ec"
}

# 2) Vendor drivers from system32\drivers
$drv = Join-Path $dest "drivers"
New-Item -ItemType Directory -Path $drv -Force | Out-Null
foreach ($f in "XiaomiBusEnumerator.sys", "xiaomisysmon.sys", "IoTDriver.sys", "IoTService.exe", "IoTAppCon.exe") {
    $src = Join-Path "C:\Windows\System32\drivers" $f
    if (-not (Test-Path $src)) { $src = Join-Path "C:\Windows\System32" $f }
    if (Test-Path $src) { Copy-Item $src $drv -Force; Write-Host "copied $f" }
}

# 3) Driver store packages (INF/CAT/SYS) for Xiaomi devices
$pkgOut = Join-Path $dest "driverstore"
New-Item -ItemType Directory -Path $pkgOut -Force | Out-Null
Get-ChildItem "C:\Windows\System32\DriverStore\FileRepository" -Directory |
    Where-Object { $_.Name -match 'xiaomi|iotservice|iotdriver|xiaomibus|xiaomisysmon' } |
    ForEach-Object {
        Copy-Item $_.FullName (Join-Path $pkgOut $_.Name) -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "copied package $($_.Name)"
    }

# 4) OEM INF files that reference Xiaomi devices
$oemOut = Join-Path $dest "inf"
New-Item -ItemType Directory -Path $oemOut -Force | Out-Null
Get-ChildItem "C:\Windows\INF\oem*.inf" -ErrorAction SilentlyContinue | ForEach-Object {
    $t = Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue
    if ($t -match 'XIAOMI|Xiaomi|IoTDriver|XiaomiBusEnumerator|xiaomisysmon') {
        Copy-Item $_.FullName $oemOut -Force
        $cat = [IO.Path]::ChangeExtension($_.FullName, ".cat")
        if (Test-Path $cat) { Copy-Item $cat $oemOut -Force }
        Write-Host "copied $($_.Name)"
    }
}

# 5) Registry export
$regFile = Join-Path $dest "MI-registry.reg"
& reg.exe export "HKLM\SOFTWARE\MI" $regFile /y | Out-Null
& reg.exe export "HKLM\SOFTWARE\WOW6432Node\MI" (Join-Path $dest "MI-registry-wow64.reg") /y | Out-Null
Write-Host "exported registry"

# 6) Service definitions
$svcOut = Join-Path $dest "services.txt"
$names = "XiaomiService","MiService","MiDeviceService","MiServiceDaemon","micont_service","dist_service","DistributedService","handoff_service","MiPlayCastService","MiRemoteDeskService","XiaomiIoTSvc","XiaomiEcIo"
foreach ($n in $names) {
    "===== $n =====" | Add-Content $svcOut
    & sc.exe qc $n 2>&1 | Add-Content $svcOut
}
Write-Host "saved service definitions"

# 7) Product uninstall info (so we can reinstall the same build if ever needed)
$uninst = Join-Path $dest "uninstall-info.txt"
Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*",
                 "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*" -ErrorAction SilentlyContinue |
    Where-Object { $_.DisplayName -match '小米|Xiaomi|MiService' } |
    Select-Object DisplayName, DisplayVersion, Publisher, UninstallString, InstallLocation |
    Format-List | Out-String | Set-Content $uninst
Write-Host "saved uninstall info"

# 8) Index
Get-ChildItem $dest -Recurse -File | Select-Object FullName, Length |
    Format-Table -AutoSize | Out-String | Set-Content (Join-Path $dest "INDEX.txt")

Write-Host "=== done: $dest ==="
Stop-Transcript | Out-Null
