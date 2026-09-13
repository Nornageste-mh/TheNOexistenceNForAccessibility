$ErrorActionPreference = 'Continue'
$G = 'D:\Steam\steamapps\common\The NOexistenceN of you AND me'
$MOD = 'D:\DSHWorkBase\noexistence_a11y\mod'
$SAVE = "$env:USERPROFILE\AppData\LocalLow\Nino\TheNOexistenceNofyouANDme\NaninovelData\Saves"
$BK = "$MOD\save_backup"

Write-Output "=== stop game ==="
Get-Process -Name 'TheNOexistenceNofyouANDme' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 4

Write-Output "=== restore save from backup, then patch playbackSpot -> Prologue1_1 line 438 ==="
Copy-Item "$BK\*" $SAVE -Recurse -Force
& "D:\python\python.exe" -c @"
import json, io, sys
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
p = r'$SAVE\1.json'
d = json.load(open(p, encoding='utf-8'))
print('  before:', json.dumps(d['playbackSpot'], ensure_ascii=False))
d['playbackSpot'] = {'scriptName': 'Prologue1_1', 'lineIndex': 438, 'inlineIndex': 0}
json.dump(d, open(p, 'w', encoding='utf-8'), ensure_ascii=False)
print('  after :', json.dumps(d['playbackSpot'], ensure_ascii=False))
"@

Remove-Item "$G\BepInEx\noexistence_a11y.log" -ErrorAction SilentlyContinue
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
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool f);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint f, IntPtr e);
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
  public static bool ForceForeground(IntPtr h) {
    ShowWindow(h, 9);
    uint fgT = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
    uint myT = GetCurrentThreadId();
    AttachThreadInput(myT, fgT, true);
    bool ok = SetForegroundWindow(h);
    AttachThreadInput(myT, fgT, false);
    System.Threading.Thread.Sleep(300);
    return GetForegroundWindow() == h;
  }
  public static void Key(byte vk) {
    keybd_event(vk, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(60);
    keybd_event(vk, 0, 2, IntPtr.Zero);
  }
}
"@

Write-Output "=== launch ==="
Start-Process "steam://rungameid/2873080"
$proc = $null; $hw = [IntPtr]::Zero
for ($i = 0; $i -lt 60; $i++) {
  Start-Sleep -Seconds 3
  $proc = Get-Process -Name 'TheNOexistenceNofyouANDme' -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($proc) { $hw = [W]::FindUnity([uint32]$proc.Id); if ($hw -ne [IntPtr]::Zero) { break } }
}
if ($proc -eq $null -or $hw -eq [IntPtr]::Zero) { Write-Output "NO WINDOW"; return }
Write-Output ("pid={0} hwnd={1}" -f $proc.Id, $hw)
Start-Sleep -Seconds 65
[W]::ForceForeground($hw) | Out-Null
Start-Sleep -Seconds 2

for ($m = 1; $m -le 10; $m++) {
  Start-Sleep -Seconds 60
  $qte = 0; $rev = 0
  if (Test-Path "$G\BepInEx\noexistence_a11y.log") {
    $c = Get-Content "$G\BepInEx\noexistence_a11y.log"
    $qte = ($c | Select-String -SimpleMatch 'QTE').Count
    $rev = ($c | Select-String -SimpleMatch 'REVEAL fresh=True').Count
  }
  Write-Output ("  minute {0}: REVEAL={1} QTE={2}" -f $m, $rev, $qte)
  if ($qte -gt 0) { Write-Output "  QTE hit, waiting 60s more"; Start-Sleep -Seconds 60; break }
}

Write-Output ""
Write-Output "=== QTE lines ==="
if (Test-Path "$G\BepInEx\noexistence_a11y.log") {
  Get-Content "$G\BepInEx\noexistence_a11y.log" -Encoding UTF8 | Select-String -SimpleMatch 'QTE' | Select-Object -First 30 | ForEach-Object { $_.Line }
  Write-Output ""
  Write-Output "=== last 25 REVEAL ==="
  Get-Content "$G\BepInEx\noexistence_a11y.log" -Encoding UTF8 | Select-String -SimpleMatch 'REVEAL fresh=True' | Select-Object -Last 25 | ForEach-Object { $_.Line }
}

Write-Output ""
Write-Output "=== stop game and restore save ==="
Get-Process -Name 'TheNOexistenceNofyouANDme' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 5
Copy-Item "$BK\*" $SAVE -Recurse -Force
Get-ChildItem $SAVE -File | ForEach-Object { Write-Output ("  restored md5 " + (Get-FileHash $_.FullName -Algorithm MD5).Hash + "  " + $_.Name) }
