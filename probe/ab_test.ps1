$ErrorActionPreference = 'Continue'
$G = 'D:\Steam\steamapps\common\The NOexistenceN of you AND me'
$P = 'D:\DSHWorkBase\noexistence_a11y\probe'
$core = "$G\BepInEx\core\Il2CppInterop.Runtime.dll"

Write-Output "=== A/B test: swap back the STOCK (unpatched) Il2CppInterop.Runtime ==="
Copy-Item $core "$P\evidence\Il2CppInterop.Runtime.PATCHED.dll" -Force
Copy-Item "$P\bepinex\BepInEx\core\Il2CppInterop.Runtime.dll" $core -Force
$vi = (Get-Item $core).VersionInfo
Write-Output ("now installed Product={0}  size={1:N0}" -f $vi.ProductVersion, (Get-Item $core).Length)

Copy-Item "$P\plugin\bin\Release\NoExistenceA11yProbe.dll" "$G\BepInEx\plugins\" -Force
Remove-Item "$G\BepInEx\probe_text.log" -ErrorAction SilentlyContinue
Remove-Item "$G\BepInEx\LogOutput.log" -ErrorAction SilentlyContinue

Write-Output "=== launch ==="
Start-Process "steam://rungameid/2873080"
$proc = $null
for ($i = 0; $i -lt 50; $i++) {
  Start-Sleep -Seconds 3
  $proc = Get-Process -Name 'TheNOexistenceNofyouANDme' -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($proc -and $proc.MainWindowHandle -ne 0) { break }
}
if (-not $proc) { Write-Output "process not started"; return }
Write-Output ("PID={0}" -f $proc.Id)
Start-Sleep -Seconds 160
$proc.Refresh()
$alive = -not $proc.HasExited
Write-Output ("process alive after 160s: {0}  (working set {1:N0} MB)" -f $alive, ($proc.WorkingSet64/1MB))
Write-Output ("probe_text.log exists: {0}" -f (Test-Path "$G\BepInEx\probe_text.log"))
if (Test-Path "$G\BepInEx\probe_text.log") {
  $l = Get-Content "$G\BepInEx\probe_text.log" -Encoding UTF8
  Write-Output ("captured lines: {0}" -f $l.Count)
  $l | Select-Object -First 12
}
Write-Output ""
Write-Output "=== Class::Init related lines in LogOutput.log ==="
Get-Content "$G\BepInEx\LogOutput.log" -Encoding UTF8 -ErrorAction SilentlyContinue |
  Select-String -Pattern 'Class::Init|exhausted|substitute|Chainloader startup complete|Fatal|Exception' |
  Select-Object -First 15 | ForEach-Object { $_.Line }
Write-Output ""
Write-Output "=== [probe] lines ==="
Get-Content "$G\BepInEx\LogOutput.log" -Encoding UTF8 -ErrorAction SilentlyContinue |
  Select-String -SimpleMatch '[probe]' | Select-Object -First 12 | ForEach-Object { $_.Line }

Write-Output ""
Write-Output "=== stop game, restore PATCHED dll, drop probe plugin ==="
Get-Process -Name 'TheNOexistenceNofyouANDme','UnityCrashHandler64' -ErrorAction SilentlyContinue |
  ForEach-Object { Stop-Process -Id $_.Id -Force }
Start-Sleep -Seconds 2
Copy-Item "$P\evidence\Il2CppInterop.Runtime.PATCHED.dll" $core -Force
Remove-Item "$G\BepInEx\plugins\NoExistenceA11yProbe.dll" -Force -ErrorAction SilentlyContinue
Write-Output ("restored Product={0}" -f (Get-Item $core).VersionInfo.ProductVersion)
