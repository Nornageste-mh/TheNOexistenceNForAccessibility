# =====================================================================
#  Assemble the release zip for v0.1.0.0
#
#  发布包 = BepInEx 6.0.0-be.788 官方包
#         + 打过补丁的 Il2CppInterop.Runtime.dll（上游 PR #277）
#         + 本补丁插件
#         + 两份文档 + licenses\（第三方许可证与声明）
#
#  不含 BepInEx/interop 与 unity-libs —— 那是首次运行时按本机游戏
#  自动生成的，不该由我们分发。
#
#  ASCII only on purpose: a .ps1 with non-ASCII text and no BOM is read
#  as GBK by Windows PowerShell and fails to parse.
# =====================================================================
$ErrorActionPreference = 'Stop'
$MOD  = Split-Path -Parent $MyInvocation.MyCommand.Path
$SRC  = Join-Path $MOD 'src\NoExistenceA11y'
$PKG  = Join-Path $MOD 'package'
$OUT  = Join-Path $MOD 'dist'
$REL  = Join-Path $OUT 'NoExistenceA11y-0.1.0.0'
$BE   = Join-Path $MOD '..\probe\bepinex_be788.zip'
$PAT  = Join-Path $MOD '..\probe\evidence\Il2CppInterop.Runtime.PATCHED.dll'
$NVDA = 'D:\DSHWorkBase\transparenther_a11y\mod\nvda_dl\x64\nvdaControllerClient.dll'

Write-Host "=== 1. build ==="
dotnet build (Join-Path $SRC 'NoExistenceA11y.csproj') -c Release -v minimal
if ($LASTEXITCODE -ne 0) { Write-Host "BUILD FAILED" -ForegroundColor Red; exit 1 }
$dll = Join-Path $SRC 'bin\Release\NoExistenceA11y.dll'
$ver = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($dll).FileVersion
Write-Host ("    NoExistenceA11y.dll  v" + $ver)

Write-Host ""
Write-Host "=== 2. layout ==="
if (Test-Path $REL) { Remove-Item $REL -Recurse -Force }
New-Item -ItemType Directory -Force -Path $REL | Out-Null

Write-Host "    extracting BepInEx ..."
Expand-Archive -Path $BE -DestinationPath $REL -Force

Write-Host "    overlaying patched interop ..."
if (-not (Test-Path $PAT)) { Write-Host "    !! patched interop not found" -ForegroundColor Red; exit 1 }
Copy-Item $PAT (Join-Path $REL 'BepInEx\core\Il2CppInterop.Runtime.dll') -Force

Write-Host "    overlaying plugin ..."
$plug = Join-Path $REL 'BepInEx\plugins'
New-Item -ItemType Directory -Force -Path $plug | Out-Null
Copy-Item $dll $plug -Force
Copy-Item $NVDA $plug -Force

Write-Host "    copying docs ..."
# 按扩展名收集，避免在 ASCII 脚本里写中文文件名
$docs = Get-ChildItem $PKG -Filter '*.md' -File
foreach ($d in $docs) {
  Copy-Item $d.FullName $REL -Force
  Write-Host ("      " + $d.Name)
}
if ($docs.Count -eq 0) { Write-Host "    !! no docs found in package/" -ForegroundColor Red; exit 1 }

Write-Host "    copying licenses ..."
$LIC = Join-Path $MOD 'licenses'
if (-not (Test-Path $LIC)) { Write-Host "    !! licenses/ not found" -ForegroundColor Red; exit 1 }
$licDst = Join-Path $REL 'licenses'
New-Item -ItemType Directory -Force -Path $licDst | Out-Null
Copy-Item (Join-Path $LIC '*') $licDst -Recurse -Force
$licCount = (Get-ChildItem $licDst -File).Count
Write-Host ("      " + $licCount + " files -> licenses\")
foreach ($must in @('THIRD-PARTY-NOTICES.txt', 'il2cppinterop-pr277.patch')) {
  if (-not (Test-Path (Join-Path $licDst $must))) { Write-Host ("    !! licenses\" + $must + " missing") -ForegroundColor Red; exit 1 }
}

Write-Host ""
Write-Host "=== 3. sanity checks ==="
$need = @(
  'winhttp.dll',
  'doorstop_config.ini',
  '.doorstop_version',
  'BepInEx\core\BepInEx.Unity.IL2CPP.dll',
  'BepInEx\core\Il2CppInterop.Runtime.dll',
  'BepInEx\plugins\NoExistenceA11y.dll',
  'BepInEx\plugins\nvdaControllerClient.dll'
)
$ok = $true
foreach ($n in $need) {
  $p = Join-Path $REL $n
  if (Test-Path $p) { Write-Host ("    [ok]   " + $n) }
  else { Write-Host ("    [MISS] " + $n) -ForegroundColor Red; $ok = $false }
}
foreach ($d in $docs) {
  if (Test-Path (Join-Path $REL $d.Name)) { Write-Host ("    [ok]   " + $d.Name) }
  else { Write-Host ("    [MISS] " + $d.Name) -ForegroundColor Red; $ok = $false }
}
if (Test-Path (Join-Path $REL 'licenses')) { Write-Host ("    [ok]   licenses\ (" + $licCount + " files)") }
else { Write-Host "    [MISS] licenses\" -ForegroundColor Red; $ok = $false }
if (Test-Path (Join-Path $REL 'BepInEx\interop')) { Write-Host "    [warn] interop should not be shipped" -ForegroundColor Yellow }
if (Test-Path (Join-Path $REL 'BepInEx\config\noexistence.a11y.cfg')) { Write-Host "    [warn] shipped config should not exist" -ForegroundColor Yellow }
if (-not $ok) { exit 1 }

Write-Host ""
Write-Host "=== 4. zip ==="
New-Item -ItemType Directory -Force -Path $OUT | Out-Null
$zip = Join-Path $OUT ("NoExistenceA11y-" + $ver + ".zip")
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $REL '*') -DestinationPath $zip -Force
$f = Get-Item $zip
Write-Host ("    " + $zip)
Write-Host ("    " + [Math]::Round($f.Length / 1MB, 1) + " MB")
