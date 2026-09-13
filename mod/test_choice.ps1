$ErrorActionPreference = 'Continue'
$G = 'D:\Steam\steamapps\common\The NOexistenceN of you AND me'
$MOD = 'D:\DSHWorkBase\noexistence_a11y\mod'

Write-Output "=== stop game ==="
Get-Process -Name 'TheNOexistenceNofyouANDme' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 4

Copy-Item "$MOD\src\NoExistenceA11y\bin\Release\NoExistenceA11y.dll" "$G\BepInEx\plugins\" -Force
Remove-Item "$G\BepInEx\noexistence_a11y.log" -ErrorAction SilentlyContinue
Remove-Item "$G\BepInEx\LogOutput.log" -ErrorAction SilentlyContinue

# launch the exe DIRECTLY so the env var reaches the game (Steam drops it)
$env:NOEXISTENCE_A11Y_TEST = '1'
Write-Output "=== launch exe directly with NOEXISTENCE_A11Y_TEST=1 ==="
$proc = Start-Process -FilePath "$G\TheNOexistenceNofyouANDme.exe" -WorkingDirectory $G -PassThru
if ($proc -eq $null) { Write-Output "LAUNCH FAILED"; return }
Write-Output ("pid=" + $proc.Id)

for ($m = 1; $m -le 14; $m++) {
  Start-Sleep -Seconds 60
  $n = 0; $ch = 0
  if (Test-Path "$G\BepInEx\noexistence_a11y.log") {
    $c = Get-Content "$G\BepInEx\noexistence_a11y.log"
    $n = $c.Count
    $ch = ($c | Select-String -SimpleMatch 'CHOICE').Count
  }
  $alive = (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue) -ne $null
  Write-Output ("  minute {0}: {1} log lines, {2} CHOICE, alive={3}" -f $m, $n, $ch, $alive)
  if (-not $alive) { break }
  if ($ch -gt 4) { Write-Output "  got choices, stopping early"; break }
}

Write-Output ""
Write-Output "=== CHOICE lines ==="
if (Test-Path "$G\BepInEx\noexistence_a11y.log") {
  Get-Content "$G\BepInEx\noexistence_a11y.log" -Encoding UTF8 | Select-String -SimpleMatch 'CHOICE' | Select-Object -First 40 | ForEach-Object { $_.Line }
  Write-Output ""
  Write-Output "=== 选项容器 dump (first block) ==="
  $c = Get-Content "$G\BepInEx\noexistence_a11y.log" -Encoding UTF8
  $i = ($c | Select-String -SimpleMatch '选项容器' | Select-Object -First 1).LineNumber
  if ($i) { $c[($i-1)..([Math]::Min($i+14, $c.Count-1))] | ForEach-Object { $_ } }
  else { Write-Output "(no container dump)" }
} else { Write-Output "(no log)" }
