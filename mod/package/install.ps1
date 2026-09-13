# =====================================================================
#  The NOexistenceN of you AND me  -  accessibility mod installer
#  ASCII only on purpose: a .ps1 with non-ASCII text and no BOM is read
#  as GBK by Windows PowerShell and fails to parse.
# =====================================================================
param(
  [string]$GameDir = '',
  [switch]$SkipInterop
)

$ErrorActionPreference = 'Stop'
$PKG = Split-Path -Parent $MyInvocation.MyCommand.Path
$ROOT = Split-Path -Parent $PKG          # mod\

function Say($m) { Write-Host $m }
function Ok($m)  { Write-Host "  [ok]   $m" }
function Warn($m){ Write-Host "  [warn] $m" -ForegroundColor Yellow }
function Die($m) { Write-Host "  [FAIL] $m" -ForegroundColor Red; exit 1 }

Say "=== The NOexistenceN of you AND me - accessibility mod installer ==="

# ---------- 1. locate the game ----------
if (-not $GameDir) {
  $cands = @(
    'D:\Steam\steamapps\common\The NOexistenceN of you AND me',
    'C:\Steam\steamapps\common\The NOexistenceN of you AND me',
    'E:\Steam\steamapps\common\The NOexistenceN of you AND me'
  )
  foreach ($c in $cands) { if (Test-Path (Join-Path $c 'TheNOexistenceNofyouANDme.exe')) { $GameDir = $c; break } }
}
if (-not $GameDir -or -not (Test-Path (Join-Path $GameDir 'TheNOexistenceNofyouANDme.exe'))) {
  Die "game not found. pass -GameDir '<path to The NOexistenceN of you AND me>'"
}
Ok "game: $GameDir"

# ---------- 2. BepInEx (IL2CPP, bleeding edge) ----------
$winhttp = Join-Path $GameDir 'winhttp.dll'
$core    = Join-Path $GameDir 'BepInEx\core'
$haveBep = (Test-Path $winhttp) -and (Test-Path (Join-Path $core 'BepInEx.Unity.IL2CPP.dll'))

if (-not $haveBep) {
  $zip = Join-Path $PKG 'bepinex_be.zip'
  if (-not (Test-Path $zip)) {
    $zip = Join-Path (Split-Path -Parent $PKG) 'probe\bepinex_be788.zip'
  }
  if (-not (Test-Path $zip)) {
    Die "BepInEx is not installed and no bepinex_be.zip was found next to this script.`n         Download BepInEx 6.0.0-be.788 (x64, IL2CPP) from https://builds.bepinex.dev/projects/bepinex_be and put it here as bepinex_be.zip"
  }
  Say "  installing BepInEx from $zip ..."
  Expand-Archive -Path $zip -DestinationPath $GameDir -Force
  if (-not (Test-Path (Join-Path $core 'BepInEx.Unity.IL2CPP.dll'))) {
    # some builds nest one level
    $inner = Get-ChildItem $GameDir -Directory -Filter 'BepInEx*' |
             Where-Object { Test-Path (Join-Path $_.FullName 'core\BepInEx.Unity.IL2CPP.dll') } |
             Select-Object -First 1
    if ($inner) { Copy-Item (Join-Path $inner.FullName '*') (Join-Path $GameDir 'BepInEx') -Recurse -Force }
  }
  if (-not (Test-Path (Join-Path $core 'BepInEx.Unity.IL2CPP.dll'))) { Die "BepInEx extraction did not produce BepInEx\core" }
  Ok "BepInEx installed"
} else {
  Ok "BepInEx already present"
}

# ---------- 3. patched Il2CppInterop.Runtime ----------
# The stock 1.5.3 build exhausts Class::Init signatures on this game's IL2CPP
# metadata (v31.1) and falls back to a substitute table. It still works, but a
# build carrying upstream PR #277 resolves cleanly. Ship the patched one.
if (-not $SkipInterop) {
  $patched = Join-Path $PKG 'Il2CppInterop.Runtime.dll'
  $target  = Join-Path $core 'Il2CppInterop.Runtime.dll'
  if (Test-Path $patched) {
    $same = $false
    if (Test-Path $target) {
      $same = (Get-FileHash $patched).Hash -eq (Get-FileHash $target).Hash
    }
    if ($same) { Ok "Il2CppInterop.Runtime.dll already patched" }
    else {
      Copy-Item $target "$target.orig" -Force -ErrorAction SilentlyContinue
      Copy-Item $patched $target -Force
      Ok "Il2CppInterop.Runtime.dll replaced (original kept as .orig)"
    }
  } else { Warn "patched Il2CppInterop.Runtime.dll not in package; stock build will be used (works, with a warning in the log)" }
}

# ---------- 4. the mod itself ----------
$plugins = Join-Path $GameDir 'BepInEx\plugins'
New-Item -ItemType Directory -Force -Path $plugins | Out-Null

Copy-Item (Join-Path $PKG 'NoExistenceA11y.dll') $plugins -Force
Ok "NoExistenceA11y.dll -> BepInEx\plugins"

$nvda = Join-Path $PKG 'nvdaControllerClient.dll'
if (Test-Path $nvda) {
  Copy-Item $nvda $plugins -Force
  Ok "nvdaControllerClient.dll -> BepInEx\plugins"
} else { Warn "nvdaControllerClient.dll missing; NVDA backend will be unavailable (SAPI fallback still works)" }

Say ""
Say "=== done ==="
Say "  config: $GameDir\BepInEx\config\noexistence.a11y.cfg"
Say "  log   : $GameDir\BepInEx\noexistence_a11y.log"
Say ""
Say "  Tab = toggle menu navigation, arrows = move, Enter/Space = activate"
Say "  1-9 = pick a dialogue option directly (no mouse needed)"
Say ""
