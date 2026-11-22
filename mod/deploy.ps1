# =====================================================================
#  Deploy the built a11y plugin into the local game installation.
#
#  Why this file exists: the tester asked to have every new build
#  deployed automatically into the game folder, and to never uninstall
#  the patch. This script only copies files into BepInEx\plugins and
#  keeps the player's existing BepInEx\config untouched.
#
#  Default source: newest mod\dist\NoExistenceA11y-*\BepInEx\plugins
#  Default target: D:\Steam\steamapps\common\The NOexistenceN of you AND me
#
#  Usage:
#    powershell -NoProfile -ExecutionPolicy Bypass -File mod\deploy.ps1
#    powershell ... -File mod\deploy.ps1 -LayoutDir mod\dist\NoExistenceA11y-0.2.1.0-exp1
#    powershell ... -File mod\deploy.ps1 -NoBackup
#
#  ASCII only on purpose: a .ps1 with non-ASCII text and no BOM is read
#  as GBK by Windows PowerShell and fails to parse.
# =====================================================================
param(
  [string]$GameDir = 'D:\Steam\steamapps\common\The NOexistenceN of you AND me',
  [string]$LayoutDir = '',
  [switch]$NoBackup
)

$ErrorActionPreference = 'Stop'
$MOD = Split-Path -Parent $MyInvocation.MyCommand.Path

# --- pick the release layout to deploy from --------------------------
if (-not $LayoutDir) {
  $candidates = @(Get-ChildItem -LiteralPath (Join-Path $MOD 'dist') -Directory -ErrorAction SilentlyContinue |
    Where-Object { Test-Path (Join-Path $_.FullName 'BepInEx\plugins\NoExistenceA11y.dll') } |
    Sort-Object LastWriteTime -Descending)
  if ($candidates.Count -eq 0) { throw 'no dist layout with BepInEx\plugins\NoExistenceA11y.dll; run release.ps1 first' }
  $LayoutDir = $candidates[0].FullName
}
$srcPlugins = Join-Path $LayoutDir 'BepInEx\plugins'
$srcDll = Join-Path $srcPlugins 'NoExistenceA11y.dll'
if (-not (Test-Path $srcDll)) { throw ("source plugin not found: " + $srcDll) }

# --- validate the game folder ----------------------------------------
$dstPlugins = Join-Path $GameDir 'BepInEx\plugins'
$exe = Join-Path $GameDir 'TheNOexistenceNofyouANDme.exe'
if (-not (Test-Path $dstPlugins)) { throw ("game BepInEx\plugins not found: " + $dstPlugins) }
if (-not (Test-Path $exe)) { throw ("game exe not found: " + $exe) }

$running = Get-Process -Name 'TheNOexistenceNofyouANDme' -ErrorAction SilentlyContinue
if ($running) { throw 'the game is running; close it before deploying' }

# --- backup the currently installed plugin ---------------------------
$dstDll = Join-Path $dstPlugins 'NoExistenceA11y.dll'
$dstHashBefore = ''
if (Test-Path $dstDll) { $dstHashBefore = (Get-FileHash -LiteralPath $dstDll -Algorithm SHA256).Hash }
$srcHashEarly = (Get-FileHash -LiteralPath $srcDll -Algorithm SHA256).Hash
if (-not $NoBackup -and (Test-Path $dstDll) -and $dstHashBefore -ne $srcHashEarly) {
  $bak = Join-Path $dstPlugins 'NoExistenceA11y.dll.bak'
  Copy-Item -LiteralPath $dstDll -Destination $bak -Force
  Write-Host ("    [backup] " + $bak)
}

# --- make Doorstop immune to a polluted Steam session -----------------
# Why: a doorstop-injected game process writes DOORSTOP_DISABLE=TRUE into its
# own environment, child processes inherit it, and if Steam is (re)started from
# that chain the whole Steam session carries it; from then on every game Steam
# launches skips injection. Setting ignore_disable_switch = true in this game's
# doorstop_config.ini makes the install load BepInEx even in a polluted session
# (same defense the aliya_a11y patch uses, verified on this machine).
$ini = Join-Path $GameDir 'doorstop_config.ini'
if (Test-Path $ini) {
  $txt = Get-Content -LiteralPath $ini -Raw
  if ($txt -match '(?m)^\s*ignore_disable_switch\s*=\s*false\s*$') {
    $txt = $txt -replace '(?m)^(\s*ignore_disable_switch\s*=\s*)false(\s*)$', '${1}true${2}'
    Set-Content -LiteralPath $ini -Value $txt -NoNewline -Encoding ASCII
    Write-Host '    [doorstop] ignore_disable_switch -> true'
  } elseif ($txt -notmatch '(?m)^\s*ignore_disable_switch\s*=') {
    $txt = $txt.TrimEnd() + [Environment]::NewLine + 'ignore_disable_switch = true' + [Environment]::NewLine
    Set-Content -LiteralPath $ini -Value $txt -NoNewline -Encoding ASCII
    Write-Host '    [doorstop] ignore_disable_switch added'
  } else {
    Write-Host '    [doorstop] ignore_disable_switch already set'
  }
} else {
  Write-Host '    [warn] doorstop_config.ini not found in game folder' -ForegroundColor Yellow
}

# --- copy plugin files (never remove anything) -----------------------
$files = @('NoExistenceA11y.dll', 'nvdaControllerClient.dll', 'ZDSRAPI_x64.dll', 'ZDSRAPI.dll')
foreach ($f in $files) {
  $src = Join-Path $srcPlugins $f
  if (-not (Test-Path $src)) {
    if ($f -eq 'NoExistenceA11y.dll') { throw ("missing " + $src) }
    Write-Host ("    [skip]   " + $f + " (not in layout)")
    continue
  }
  Copy-Item -LiteralPath $src -Destination (Join-Path $dstPlugins $f) -Force
  Write-Host ("    [copy]   " + $f)
}

# --- verify ----------------------------------------------------------
$vi = (Get-Item $dstDll).VersionInfo
$srcHash = (Get-FileHash -LiteralPath $srcDll -Algorithm SHA256).Hash
$dstHash = (Get-FileHash -LiteralPath $dstDll -Algorithm SHA256).Hash
$size = (Get-Item $dstDll).Length
Write-Host ""
Write-Host ("    deployed: " + $dstDll)
Write-Host ("    size: " + $size + " bytes")
Write-Host ("    FileVersion: " + $vi.FileVersion + "  ProductVersion: " + $vi.ProductVersion)
Write-Host ("    sha256: " + $dstHash)
$label = ''
try {
  $pluginSrc = Join-Path $MOD 'src\NoExistenceA11y\Plugin.cs'
  if (Test-Path $pluginSrc) {
    $m = Select-String -LiteralPath $pluginSrc -Pattern 'VersionLabel\s*=\s*"([^"]+)"' | Select-Object -First 1
    if ($m) { $label = $m.Matches[0].Groups[1].Value }
  }
} catch { }
if ($label) { Write-Host ("    label: " + $label) }
if ($srcHash -ne $dstHash) { throw 'deployed dll hash does not match the layout' }
Write-Host "    [ok] hash matches the release layout"
Write-Host "    note: BepInEx\config was NOT touched (player settings preserved)"
