$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class W2 {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

$G = 'D:\Steam\steamapps\common\The NOexistenceN of you AND me'
$P = 'D:\DSHWorkBase\noexistence_a11y\probe'
$probeLog = "$G\BepInEx\probe_text.log"

Write-Output "=== deploy v2 plugin ==="
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
Write-Output ("PID={0} HWND={1}" -f $proc.Id, $proc.MainWindowHandle)

Write-Output "=== wait 100s to reach title screen ==="
Start-Sleep -Seconds 100
$proc.Refresh()

Write-Output "=== probe_text.log now ==="
if (Test-Path $probeLog) { Get-Content $probeLog -TotalCount 40 } else { Write-Output "(none)" }
Write-Output ""
Write-Output "=== [probe] lines in LogOutput.log ==="
Get-Content "$G\BepInEx\LogOutput.log" -ErrorAction SilentlyContinue | Select-String -SimpleMatch '[probe]' | Select-Object -Last 25 | ForEach-Object { $_.Line }

Write-Output ""
Write-Output "=== screenshot game window ==="
$r = New-Object W2+RECT
[void][W2]::ShowWindow($proc.MainWindowHandle, 9)
[void][W2]::SetForegroundWindow($proc.MainWindowHandle)
Start-Sleep -Seconds 2
if ([W2]::GetWindowRect($proc.MainWindowHandle, [ref]$r)) {
  $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
  Write-Output ("rect L={0} T={1} W={2} H={3}" -f $r.Left, $r.Top, $w, $h)
  $bmp = New-Object System.Drawing.Bitmap($w, $h)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
  $out = "$P\shot_title.png"
  $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  Write-Output ("saved: {0} ({1} bytes)" -f $out, (Get-Item $out).Length)
} else { Write-Output "GetWindowRect failed" }
