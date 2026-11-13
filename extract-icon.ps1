param(
    [string]$Source = "C:\Program Files\MI\XiaomiPCManager\6.0.2.30\MiSmartShare\MiDropShellExt.dll",
    [string]$OutDir = "C:\Users\ming_\Documents\deepseek-harness\default-workspace\MiControl\assets"
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

Add-Type -Language CSharp @"
using System;
using System.Runtime.InteropServices;

public static class IconExtract
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint PrivateExtractIcons(
        string szFileName, int nIconIndex, int cxIcon, int cyIcon,
        IntPtr[] phicon, uint[] piconid, uint nIcons, uint flags);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);
}
"@

foreach ($size in 16, 32, 48, 256) {
    $handles = New-Object IntPtr[] 1
    $ids = New-Object uint32[] 1
    $count = [IconExtract]::PrivateExtractIcons($Source, 0, $size, $size, $handles, $ids, 1, 0)
    if ($count -eq 0 -or $handles[0] -eq [IntPtr]::Zero) {
        Write-Host "size $size : not available"
        continue
    }

    $icon = [System.Drawing.Icon]::FromHandle($handles[0])
    $bmp = $icon.ToBitmap()
    $path = Join-Path $OutDir ("mi-$size.png")
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $icon.Dispose()
    [IconExtract]::DestroyIcon($handles[0]) | Out-Null
    Write-Host "saved $path"
}

Get-ChildItem $OutDir -Filter "mi-*.png" | Select-Object Name, Length
