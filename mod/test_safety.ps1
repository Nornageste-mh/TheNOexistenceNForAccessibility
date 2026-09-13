$ErrorActionPreference = 'Continue'
$G = 'D:\Steam\steamapps\common\The NOexistenceN of you AND me'
$MOD = 'D:\DSHWorkBase\noexistence_a11y\mod'
$SAVE = "$env:USERPROFILE\AppData\LocalLow\Nino\TheNOexistenceNofyouANDme\NaninovelData\Saves"
$BK = "$MOD\save_backup"

Write-Output "=== stop game ==="
Get-Process -Name 'TheNOexistenceNofyouANDme' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 4

Write-Output "=== backup saves ==="
Remove-Item $BK -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $BK | Out-Null
Copy-Item "$SAVE\*" $BK -Recurse -Force
Get-ChildItem $BK | ForEach-Object { Write-Output ("  " + $_.Name + "  " + $_.Length) }
Get-ChildItem $SAVE -File | ForEach-Object {
  Write-Output ("  md5 " + (Get-FileHash $_.FullName -Algorithm MD5).Hash + "  " + $_.Name)
}

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
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool f);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
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
  public static void Click(int x, int y) {
    SetCursorPos(x, y); System.Threading.Thread.Sleep(80);
    mouse_event(0x0002, 0, 0, 0, IntPtr.Zero); System.Threading.Thread.Sleep(60);
    mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
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

Start-Sleep -Seconds 62
Write-Output ("ForceForeground -> " + [W]::ForceForeground($hw))
Start-Sleep -Seconds 2

Write-Output "=== Tab (main menu) ==="
[W]::Key(0x09); Start-Sleep -Seconds 3
[W]::Key(0x28); Start-Sleep -Seconds 2
[W]::Key(0x28); Start-Sleep -Seconds 2
[W]::Key(0x09); Start-Sleep -Seconds 2

Write-Output "=== click CONTINUE (460,491) -> loads autosave ==="
[W]::Click(460, 491)
Start-Sleep -Seconds 45

Write-Output ""
Write-Output "=== UiNav scan summaries ==="
Get-Content "$G\BepInEx\LogOutput.log" -Encoding UTF8 | Select-String -SimpleMatch 'Selectable 总数=' | Select-Object -Last 6 | ForEach-Object { $_.Line }
Write-Output ""
Write-Output "=== UiNav group listings ==="
Get-Content "$G\BepInEx\LogOutput.log" -Encoding UTF8 | Select-String -SimpleMatch '扫描到' | Select-Object -Last 6 | ForEach-Object { $_.Line }
Write-Output ""
Write-Output "=== spoiler gate hits (拦下) ==="
if (Test-Path "$G\BepInEx\noexistence_a11y.log") {
  Get-Content "$G\BepInEx\noexistence_a11y.log" -Encoding UTF8 | Select-String -SimpleMatch '拦下' | Select-Object -First 20 | ForEach-Object { $_.Line }
  Write-Output ""
  Write-Output "=== what actually got read after load ==="
  Get-Content "$G\BepInEx\noexistence_a11y.log" -Encoding UTF8 | Select-String -SimpleMatch 'REVEAL fresh=True' | Select-Object -First 20 | ForEach-Object { $_.Line }
}
