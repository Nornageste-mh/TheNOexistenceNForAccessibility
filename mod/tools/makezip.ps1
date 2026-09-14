# 打 ZIP：条目名必须用正斜杠。
#
# 为什么不用 Compress-Archive：Windows PowerShell 5.1 的它底层是 .NET Framework 的
# ZipFile.CreateFromDirectory，用 Path.DirectorySeparatorChar 生成条目名，于是包里
# 的路径长这样：
#
#     条目总数 = 255   含反斜杠 = 249   含正斜杠 = 0
#       BepInEx\core\BepInEx.Unity.IL2CPP.dll
#
# ZIP 规范里路径分隔符是 '/'。Windows 资源管理器能容忍反斜杠，7-Zip 一般也行，
# Windows 上的 Python zipfile 也会自己换回来（ZipInfo.__init__ 里有
# replace(os.sep, "/")，而 Windows 的 os.sep 正好是反斜杠）——
# 但 Linux/macOS 的 unzip 与非 Windows 的 Python 不会，会把
# "BepInEx\core\x.dll" 当成一个文件名里带反斜杠的平铺条目，解出来是一坨。
#
# 所以这里手工建条目，名字自己保证是 '/'。不依赖 7-Zip 等外部程序，
# 任何装了 .NET 的机器都能复现。
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$Zip
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (-not (Test-Path -LiteralPath $Source)) { throw "source dir not found: $Source" }
$srcFull = (Resolve-Path -LiteralPath $Source).Path.TrimEnd('\')

if (Test-Path -LiteralPath $Zip) { Remove-Item -LiteralPath $Zip -Force }
$zipDir = Split-Path $Zip -Parent
if (-not (Test-Path -LiteralPath $zipDir)) { New-Item -ItemType Directory -Force -Path $zipDir | Out-Null }

$fs = [System.IO.File]::Open($Zip, [System.IO.FileMode]::CreateNew)
try {
    $arch = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create, $true)

    # 目录条目：空目录不写进去就会在解压时消失
    foreach ($d in (Get-ChildItem -LiteralPath $Source -Recurse -Directory -Force)) {
        $rel = $d.FullName.Substring($srcFull.Length + 1).Replace('\', '/') + '/'
        $null = $arch.CreateEntry($rel, [System.IO.Compression.CompressionLevel]::NoCompression)
    }

    foreach ($f in (Get-ChildItem -LiteralPath $Source -Recurse -File -Force)) {
        $rel = $f.FullName.Substring($srcFull.Length + 1).Replace('\', '/')
        $entry = $arch.CreateEntry($rel, [System.IO.Compression.CompressionLevel]::Optimal)
        $es = $entry.Open()
        try {
            $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
            $es.Write($bytes, 0, $bytes.Length)
        } finally { $es.Dispose() }
    }

    $arch.Dispose()
} finally { $fs.Dispose() }

Write-Host ("zip: " + $Zip)
Write-Host ("     " + [Math]::Round((Get-Item -LiteralPath $Zip).Length / 1MB, 2) + " MB")
