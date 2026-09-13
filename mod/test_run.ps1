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
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool f);
  public static bool ForceForeground(IntPtr h) {
    ShowWindow(h, 9);                       // SW_RESTORE
    IntPtr fg = GetForegroundWindow();
    uint fgT = GetWindowThreadProcessId(fg, IntPtr.Zero);
    uint myT = GetCurrentThreadId();
    AttachThreadInput(myT, fgT, true);
    bool ok = SetForegroundWindow(h);
    AttachThreadInput(myT, fgT, false);
    System.Threading.Thread.Sleep(300);
    return ok && GetForegroundWindow() == h;
  }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint f, IntPtr e);
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
  public static void Click(int x, int y) {
    SetCursorPos(x, y); System.Threading.Thread.Sleep(70);
    mouse_event(0x0002, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(50);
    mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
  }
  public static void Key(byte vk) {
    keybd_event(vk, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(40);
    keybd_event(vk, 0, 2, IntPtr.Zero);
  }
}
"@

function Shot($path) {
  $r = New-Object W+RECT
  [W]::GetWindowRect($hw, [ref]$r) | Out-Null
  $bmp = New-Object System.Drawing.Bitmap(($r.R - $r.L), ($r.B - $r.T))
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $dc = $g.GetHdc()
  [W]::PrintWindow($hw, $dc, 2) | Out-Null
  $g.ReleaseHdc($dc); $g.Dispose()
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  Write-Output ("  shot -> " + $path)
}

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

Write-Output "=== wait 60s for boot to main menu ==="
Start-Sleep -Seconds 60
$fg = [W]::ForceForeground($hw)
Write-Output ("  ForceForeground -> " + $fg)
Start-Sleep -Seconds 2
Shot "$MOD\shot_1_menu.png"

Write-Output "=== click CONTINUE (460,491) ==="
[W]::Click(460, 491)
Start-Sleep -Seconds 12
Shot "$MOD\shot_2_ingame.png"

Write-Output "=== advance 45 times ==="
for ($i = 1; $i -le 45; $i++) {
  [W]::Click(960, 950)
  Start-Sleep -Milliseconds 180
  [W]::Key(0x20)
  if ($i % 9 -eq 0) { Write-Output ("  step {0}" -f $i); Shot ("$MOD\shot_step{0}.png" -f $i) }
  Start-Sleep -Seconds 2
}
Shot "$MOD\shot_final.png"

Write-Output ""
Write-Output "=== a11y diag log ==="
if (Test-Path "$G\BepInEx\noexistence_a11y.log") { Get-Content "$G\BepInEx\noexistence_a11y.log" -Encoding UTF8 -TotalCount 100 }
else { Write-Output "(none)" }
