$ErrorActionPreference = 'Continue'
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class Win {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
}
"@

$G  = 'D:\Steam\steamapps\common\The NOexistenceN of you AND me'
$P  = 'D:\DSHWorkBase\noexistence_a11y\probe'
$log = "$G\BepInEx\probe_text.log"
$VK_RETURN = 0x0D; $VK_SPACE = 0x20

function Key([byte]$vk, [int]$times, [int]$gapMs) {
  for ($i = 0; $i -lt $times; $i++) {
    [Win]::keybd_event($vk, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [Win]::keybd_event($vk, 0, 2, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds $gapMs
  }
}
function ProbeLines { if (Test-Path $log) { (Get-Content $log | Where-Object { $_ -match "`t" }).Count } else { -1 } }

Write-Output "=== 部署插件 ==="
Copy-Item "$P\plugin\bin\Release\NoExistenceA11yProbe.dll" "$G\BepInEx\plugins\" -Force
Remove-Item $log -ErrorAction SilentlyContinue
Write-Output "已放入 BepInEx\plugins\"

Write-Output "=== 启动游戏 ==="
Start-Process "steam://rungameid/2873080"

# 等进程 + 引擎初始化
$proc = $null
for ($i = 0; $i -lt 40; $i++) {
  Start-Sleep -Seconds 3
  $proc = Get-Process -Name 'TheNOexistenceNofyouANDme' -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($proc -and $proc.MainWindowHandle -ne 0) { break }
}
if (-not $proc) { Write-Output "进程未启动，中止"; return }
Write-Output ("进程 PID={0} 窗口句柄={1}" -f $proc.Id, $proc.MainWindowHandle)

# 等 Naninovel 初始化 + 插件订阅
Start-Sleep -Seconds 45
Write-Output ("等待后探针行数: {0}" -f (ProbeLines))

# 聚焦并推进
Write-Output "=== 聚焦窗口并发送按键 ==="
[Win]::ShowWindow($proc.MainWindowHandle, 9) | Out-Null   # SW_RESTORE
[Win]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
Start-Sleep -Seconds 2
Key $VK_RETURN 2 1500
Write-Output ("回车后行数: {0}" -f (ProbeLines))
Key $VK_SPACE 25 1300
Write-Output ("空格后行数: {0}" -f (ProbeLines))

Write-Output ""
Write-Output "=== BepInEx LogOutput.log 中探针相关行 ==="
Get-Content "$G\BepInEx\LogOutput.log" -ErrorAction SilentlyContinue | Select-String -SimpleMatch '[probe]' | Select-Object -First 30 | ForEach-Object { $_.Line }
Write-Output ""
Write-Output "=== probe_text.log 内容（前 60 行）==="
if (Test-Path $log) { Get-Content $log -TotalCount 60 } else { Write-Output "(文件不存在)" }
