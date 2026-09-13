$ErrorActionPreference = 'Continue'
$G = 'D:\Steam\steamapps\common\The NOexistenceN of you AND me'
$MOD = 'D:\DSHWorkBase\noexistence_a11y\mod'

Write-Output "=== stop game ==="
Get-Process -Name 'TheNOexistenceNofyouANDme' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 4

Write-Output "=== deploy ==="
Copy-Item "$MOD\src\NoExistenceA11y\bin\Release\NoExistenceA11y.dll" "$G\BepInEx\plugins\" -Force
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

Write-Output "=== launch ==="
Start-Process "steam://rungameid/2873080"
$proc = $null; $hw = [IntPtr]::Zero
for ($i = 0; $i -lt 60; $i++) {
  Start-Sleep -Seconds 3
  $proc = Get-Process -Name 'TheNOexistenceNofyouANDme' -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($proc) { $hw = [W]::FindUnity([uint32]$proc.Id); if ($hw -ne [IntPtr]::Zero) { break } }
}
if ($proc -eq $null -or $hw -eq [IntPtr]::Zero) { Write-Output "NO WINDOW"; return }
Write-Output ("pid={0} hwnd={1} - letting autoplay drive for 11 minutes" -f $proc.Id, $hw)

for ($m = 1; $m -le 24; $m++) {
  Start-Sleep -Seconds 60
  $n = 0
  if (Test-Path "$G\BepInEx\noexistence_a11y.log") { $n = (Get-Content "$G\BepInEx\noexistence_a11y.log").Count }
  Write-Output ("  minute {0}: {1} log lines" -f $m, $n)
}

Write-Output ""
Write-Output "=== stats ==="
$log = "$G\BepInEx\noexistence_a11y.log"
if (Test-Path $log) {
  $c = Get-Content $log -Encoding UTF8
  Write-Output ("total lines: " + $c.Count)
  Write-Output ("EVENT  : " + ($c | Select-String -SimpleMatch 'EVENT' ).Count)
  Write-Output ("REVEAL : " + ($c | Select-String -SimpleMatch 'REVEAL' ).Count)
  Write-Output ("voiced : " + ($c | Select-String -SimpleMatch 'voiced=True').Count)
  Write-Output ("tts    : " + ($c | Select-String -SimpleMatch 'voiced=False').Count)
  Write-Output ("dup    : " + ($c | Select-String -SimpleMatch ' dup ').Count)
  Write-Output ("errors : " + ($c | Select-String -SimpleMatch 'UiNav:').Count)
} else { Write-Output "(no log)" }

Write-Output ""
Write-Output "=== first 60 REAL reveal lines ==="
Get-Content $log -Encoding UTF8 | Select-String -SimpleMatch 'REVEAL fresh=True' | Select-Object -First 60 | ForEach-Object { $_.Line }
