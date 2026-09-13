$ErrorActionPreference = 'Continue'
$G = 'D:\Steam\steamapps\common\The NOexistenceN of you AND me'
$MOD = 'D:\DSHWorkBase\noexistence_a11y\mod'

Write-Output "=== stop game ==="
Get-Process -Name 'TheNOexistenceNofyouANDme' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 4

Copy-Item "$MOD\src\NoExistenceA11y\bin\Release\NoExistenceA11y.dll" "$G\BepInEx\plugins\" -Force
Remove-Item "$G\BepInEx\noexistence_a11y.log" -ErrorAction SilentlyContinue
Remove-Item "$G\BepInEx\LogOutput.log" -ErrorAction SilentlyContinue

Add-Type -AssemblyName System.Drawing
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
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
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
    IntPtr fg = GetForegroundWindow();
    uint fgT = GetWindowThreadProcessId(fg, IntPtr.Zero);
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

function Shot($path, $hw) {
  $r = New-Object W+RECT
  [W]::GetWindowRect($hw, [ref]$r) | Out-Null
  $bmp = New-Object System.Drawing.Bitmap(($r.R - $r.L), ($r.B - $r.T))
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $dc = $g.GetHdc(); [W]::PrintWindow($hw, $dc, 2) | Out-Null; $g.ReleaseHdc($dc); $g.Dispose()
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}

Write-Output "=== launch (NO autostart) ==="
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
Write-Output ("ForceForeground -> " + [W]::ForceForeground($hw))
Start-Sleep -Seconds 2
Shot "$MOD\shot_menu_before.png" $hw

Write-Output "=== press Tab, then Down x3, Up x1, then Tab off, Tab on again ==="
[W]::Key(0x09)                 # Tab
Start-Sleep -Seconds 3
[W]::Key(0x28); Start-Sleep -Seconds 2      # Down
[W]::Key(0x28); Start-Sleep -Seconds 2
[W]::Key(0x28); Start-Sleep -Seconds 2
[W]::Key(0x26); Start-Sleep -Seconds 2      # Up
[W]::Key(0x0D); Start-Sleep -Seconds 4      # Enter (activate)
[W]::Key(0x09); Start-Sleep -Seconds 3      # Tab off
[W]::Key(0x09); Start-Sleep -Seconds 3      # Tab on again
Shot "$MOD\shot_menu_after.png" $hw

Write-Output ""
Write-Output "=== UiNav scan summaries ==="
Get-Content "$G\BepInEx\LogOutput.log" -Encoding UTF8 | Select-String -SimpleMatch '[UiNav]' | ForEach-Object { $_.Line } | Select-Object -First 40
