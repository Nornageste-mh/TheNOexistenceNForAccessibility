# =====================================================================
#  Build the mod and assemble a distributable package.
#  ASCII only (see install.ps1 for why).
# =====================================================================
$ErrorActionPreference = 'Stop'
$MOD = Split-Path -Parent $MyInvocation.MyCommand.Path
$PKG = Join-Path $MOD 'package'
$OUT = Join-Path $MOD 'dist'

Write-Host "=== build ==="
dotnet build (Join-Path $MOD 'src\NoExistenceA11y\NoExistenceA11y.csproj') -c Release -v minimal
if ($LASTEXITCODE -ne 0) { Write-Host "BUILD FAILED" -ForegroundColor Red; exit 1 }

Write-Host ""
Write-Host "=== assemble package ==="
New-Item -ItemType Directory -Force -Path $PKG | Out-Null

$dll = Join-Path $MOD 'src\NoExistenceA11y\bin\Release\NoExistenceA11y.dll'
Copy-Item $dll $PKG -Force
Write-Host ("  NoExistenceA11y.dll      " + (Get-Item $dll).Length)

$nvda = 'D:\DSHWorkBase\transparenther_a11y\mod\nvda_dl\x64\nvdaControllerClient.dll'
if (Test-Path $nvda) {
  Copy-Item $nvda $PKG -Force
  Write-Host ("  nvdaControllerClient.dll " + (Get-Item $nvda).Length)
} else { Write-Host "  [warn] nvdaControllerClient.dll source not found" -ForegroundColor Yellow }

$patched = 'D:\DSHWorkBase\noexistence_a11y\probe\evidence\Il2CppInterop.Runtime.PATCHED.dll'
if (Test-Path $patched) {
  Copy-Item $patched (Join-Path $PKG 'Il2CppInterop.Runtime.dll') -Force
  Write-Host ("  Il2CppInterop.Runtime.dll " + (Get-Item $patched).Length)
} else { Write-Host "  [warn] patched interop dll not found" -ForegroundColor Yellow }

$be = 'D:\DSHWorkBase\noexistence_a11y\probe\bepinex_be788.zip'
if (Test-Path $be) {
  Copy-Item $be (Join-Path $PKG 'bepinex_be.zip') -Force
  Write-Host ("  bepinex_be.zip           " + (Get-Item $be).Length)
}

Write-Host ""
Write-Host "=== zip ==="
New-Item -ItemType Directory -Force -Path $OUT | Out-Null
$zip = Join-Path $OUT 'NoExistenceA11y.zip'
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $PKG '*') -DestinationPath $zip -Force
Write-Host ("  " + $zip + "  " + (Get-Item $zip).Length + " bytes")
Write-Host ""
Write-Host "package contents:"
Get-ChildItem $PKG | ForEach-Object { Write-Host ("  " + $_.Name) }
