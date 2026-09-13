$ErrorActionPreference = 'Continue'
$G = 'D:\Steam\steamapps\common\The NOexistenceN of you AND me'
$P = 'D:\DSHWorkBase\noexistence_a11y\probe'

Write-Output "=== stop game ==="
Get-Process -Name 'TheNOexistenceNofyouANDme','UnityCrashHandler64' -ErrorAction SilentlyContinue |
  ForEach-Object { Stop-Process -Id $_.Id -Force; "killed $($_.ProcessName)" }
Start-Sleep -Seconds 3

Write-Output ""
Write-Output "=== preserve evidence (UTF-8) ==="
New-Item -ItemType Directory -Force -Path "$P\evidence" | Out-Null
Copy-Item "$G\BepInEx\probe_text.log" "$P\evidence\probe_text.log" -Force
Copy-Item "$G\BepInEx\LogOutput.log"  "$P\evidence\LogOutput.log"  -Force
Copy-Item "$G\BepInEx\interop\assembly-hash.txt" "$P\evidence\" -Force -ErrorAction SilentlyContinue
$lines = Get-Content "$G\BepInEx\probe_text.log" -Encoding UTF8
Write-Output ("captured lines: {0}" -f $lines.Count)

Write-Output ""
Write-Output "=== EVENT lines (author + script + lineId) ==="
$lines | Where-Object { $_ -like 'EVENT*' } | Select-Object -First 25

Write-Output ""
Write-Output "=== REVEAL lines (full display text, first 25) ==="
$lines | Where-Object { $_ -like 'REVEAL*' } | Select-Object -First 25

Write-Output ""
Write-Output "=== distinct authors seen ==="
$lines | Where-Object { $_ -like 'EVENT*' } | ForEach-Object { ($_ -split "`t")[1] } | Group-Object | ForEach-Object { "  '{0}' x{1}" -f $_.Name, $_.Count }

Write-Output ""
Write-Output "=== remove auto-clicking probe plugin (keep BepInEx) ==="
Remove-Item "$G\BepInEx\plugins\NoExistenceA11yProbe.dll" -Force -ErrorAction SilentlyContinue
Get-ChildItem "$G\BepInEx\plugins" -File | Select-Object Name
Write-Output "(plugins dir listed above; empty = no plugin will run)"
