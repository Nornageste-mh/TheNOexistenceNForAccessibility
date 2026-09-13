$ErrorActionPreference = 'Continue'
$G = 'D:\Steam\steamapps\common\The NOexistenceN of you AND me'
$P = 'D:\DSHWorkBase\noexistence_a11y\probe'
$probeLog = "$G\BepInEx\probe_text.log"

Write-Output "=== deploy v3 plugin ==="
Copy-Item "$P\plugin\bin\Release\NoExistenceA11yProbe.dll" "$G\BepInEx\plugins\" -Force
Remove-Item $probeLog -ErrorAction SilentlyContinue
Write-Output "ok"

Write-Output "=== launch game ==="
Start-Process "steam://rungameid/2873080"
$proc = $null
for ($i = 0; $i -lt 50; $i++) {
  Start-Sleep -Seconds 3
  $proc = Get-Process -Name 'TheNOexistenceNofyouANDme' -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($proc -and $proc.MainWindowHandle -ne 0) { break }
}
if (-not $proc) { Write-Output "process not started"; return }
Write-Output ("PID={0}" -f $proc.Id)

Write-Output "=== wait 150s (engine init + auto click + capture) ==="
Start-Sleep -Seconds 150

Write-Output ""
Write-Output "=== probe_text.log ==="
if (Test-Path $probeLog) { Get-Content $probeLog -TotalCount 80 } else { Write-Output "(none)" }

Write-Output ""
Write-Output "=== [probe] lines in LogOutput.log ==="
Get-Content "$G\BepInEx\LogOutput.log" -ErrorAction SilentlyContinue | Select-String -SimpleMatch '[probe]' | Select-Object -Last 40 | ForEach-Object { $_.Line }
