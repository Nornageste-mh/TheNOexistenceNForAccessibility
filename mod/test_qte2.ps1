$ErrorActionPreference = 'Continue'
$G = 'D:\Steam\steamapps\common\The NOexistenceN of you AND me'
$MOD = 'D:\DSHWorkBase\noexistence_a11y\mod'
$SAVE = "$env:USERPROFILE\AppData\LocalLow\Nino\TheNOexistenceNofyouANDme\NaninovelData\Saves"
$BK = "$MOD\save_backup2"
$LOG = "$G\BepInEx\noexistence_a11y.log"

function Restore-Everything {
  Write-Output ""
  Write-Output "=== cleanup ==="
  Get-Process -Name 'TheNOexistenceNofyouANDme' -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep -Seconds 5
  if (Test-Path $BK) { Copy-Item "$BK\*" $SAVE -Recurse -Force; Write-Output "  save restored" }
  Copy-Item "$MOD\cfg_normal.cfg" "$G\BepInEx\config\noexistence.a11y.cfg" -Force
  Write-Output "  config restored (test flags off)"
  Get-ChildItem $SAVE -File | ForEach-Object { Write-Output ("    " + (Get-FileHash $_.FullName -Algorithm MD5).Hash + "  " + $_.Name) }
}

Write-Output "=== stop game ==="
Get-Process -Name 'TheNOexistenceNofyouANDme' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 4

Write-Output "=== backup CURRENT save ==="
Remove-Item $BK -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $BK | Out-Null
Copy-Item "$SAVE\*" $BK -Recurse -Force
Get-ChildItem $BK -File | ForEach-Object { Write-Output ("  " + (Get-FileHash $_.FullName -Algorithm MD5).Hash + "  " + $_.Name) }

Write-Output ""
Write-Output "=== deploy dll + test config + patch save ==="
Copy-Item "$MOD\src\NoExistenceA11y\bin\Release\NoExistenceA11y.dll" "$G\BepInEx\plugins\" -Force
Copy-Item "$MOD\cfg_test.cfg" "$G\BepInEx\config\noexistence.a11y.cfg" -Force
& "D:\python\python.exe" -c @"
import json, io, sys
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
p = r'$SAVE\1.json'
d = json.load(open(p, encoding='utf-8'))
print('  before:', json.dumps(d['playbackSpot'], ensure_ascii=False))
d['playbackSpot'] = {'scriptName': 'Prologue1_6', 'lineIndex': 360, 'inlineIndex': 0}
json.dump(d, open(p, 'w', encoding='utf-8'), ensure_ascii=False)
print('  after :', json.dumps(d['playbackSpot'], ensure_ascii=False))
"@

Remove-Item $LOG -ErrorAction SilentlyContinue
Remove-Item "$G\BepInEx\LogOutput.log" -ErrorAction SilentlyContinue

Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public class W {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
  public static IntPtr FindUnity(uint want) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => {
      uint pid; GetWindowThreadProcessId(h, out pid);
      if (pid != want) return true;
      var c = new StringBuilder(256); GetClassNameW(h, c, 256);
      if (c.ToString() == "UnityWndClass") { found = h; return false; }
      return true;
    }, IntPtr.Zero);
    return found;
  }
}
"@

if (-not ([System.Management.Automation.PSTypeName]'W').Type) { Write-Output "Add-Type FAILED"; Restore-Everything; return }

Write-Output "=== launch ==="
Start-Process "steam://rungameid/2873080"
$proc = $null; $hw = [IntPtr]::Zero
for ($i = 0; $i -lt 60; $i++) {
  Start-Sleep -Seconds 3
  $proc = Get-Process -Name 'TheNOexistenceNofyouANDme' -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($proc) { $hw = [W]::FindUnity([uint32]$proc.Id); if ($hw -ne [IntPtr]::Zero) { break } }
}
if ($proc -eq $null -or $hw -eq [IntPtr]::Zero) { Write-Output "NO WINDOW"; Restore-Everything; return }
Write-Output ("pid={0} hwnd={1}" -f $proc.Id, $hw)

for ($m = 1; $m -le 14; $m++) {
  Start-Sleep -Seconds 60
  $q = 0; $rev = 0
  if (Test-Path $LOG) {
    $c = Get-Content $LOG
    $q = ($c | Select-String -SimpleMatch 'QTE').Count
    $rev = ($c | Select-String -SimpleMatch 'REVEAL fresh=True').Count
  }
  Write-Output ("  minute {0}: REVEAL={1} QTE={2}" -f $m, $rev, $q)
  if ($q -gt 0) { Write-Output "  >>> QTE 出现，再观察 3 分钟"; Start-Sleep -Seconds 180; break }
}

Write-Output ""
Write-Output "=== QTE 相关日志 ==="
if (Test-Path $LOG) { Get-Content $LOG -Encoding UTF8 | Select-String -SimpleMatch 'QTE' | Select-Object -First 60 | ForEach-Object { $_.Line } }
else { Write-Output "  (no log)" }
Write-Output ""
Write-Output "=== 这段前后读了什么（最后 35 行）==="
if (Test-Path $LOG) { Get-Content $LOG -Encoding UTF8 | Select-String -SimpleMatch 'REVEAL fresh=True' | Select-Object -Last 35 | ForEach-Object { $_.Line } }

Restore-Everything
