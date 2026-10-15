using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace NoExistenceA11y
{
    // ========================================================================
    // A11yFramework · L1 平台层（**冻结层**，逐作整份复用，不重写）
    //
    // 来源：三个已发布仓库里最完整的一份（《钟塔》BelfryForAccessibility），
    //       并已按《无障碍补丁流水线》§2.2 把对 Plugin.* 的引用改指 A11yHost.*。
    //
    // 逐作使用时**只允许改一处**：
    //   namespace —— 由 tools/new_game.py 自动替换，不要手改
    //   本文件**不引用** A11yHost / Plugin / UnityEngine / BepInEx（见下），
    //   所以从 Framework 铺到各补丁仓库时，除了 namespace 之外应当**逐字节一致**。
    //
    // 纪律：本层**绝不允许**引用任何游戏类型（Assembly-CSharp 里的任何东西）、
    //       任何逐作配置项、Il2CppInterop / Il2CppSystem。
    //       由 tools/compile_check.ps1 编译验证（不引用 Assembly-CSharp）。
    //
    // 本文件另有一条自律（0.2.0.0 起）：**不引用 UnityEngine、不引用 A11yHost、不引用 BepInEx**。
    //       理由不是洁癖，是为了让它能被一个**独立的控制台自检程序**直接编译并驱动
    //       （见 tools/zdsr_selftest/）—— 争渡的授权/运行状态无法在开发机上稳定复现，
    //       所以「选路与失败路径」必须靠桩 DLL 机械验证，而不是靠「跑一遍游戏看看」。
    //       日志出口由调用方（Speech.cs）负责，本文件只写 LastError。
    // ========================================================================
    /// <summary>
    /// 争渡读屏（ZDSR）ZDSRAPI 封装（<see cref="Speech"/> 的「争渡」后端）。
    ///
    /// === 为什么要有这一级 ===
    ///
    /// 争渡读屏是国内使用最广的读屏软件，而平台层原有的三级后端里**没有一级**能说话给它听：
    ///
    ///   · Tolk   —— 需要用户自己放 Tolk.dll，且官方不提供预编译二进制；
    ///   · NVDA   —— nvdaControllerClient 只喂 NVDA；
    ///   · SAPI   —— 系统语音，能出声，但**不是读屏的语音**：音色/语速/音量与用户
    ///               平时听的不是一套，也不走读屏的排队与点显器。
    ///
    /// 结果就是：只装争渡的玩家听到的是「另一个声音」，而不是自己读屏的声音。
    /// 这一级把争渡官方的 ZDSRAPI 接进来，让争渡用户听到自己的读屏语音。
    ///
    /// === 官方接口（《争渡读屏API（ZDSRAPI）》，版本 1）===
    ///
    ///     int  WINAPI InitTTS(int type, const WCHAR* channelName, BOOL bKeyDownInterrupt)
    ///                  type: 0 读屏通道 / 1 独立通道；可重复调用以改参数
    ///                  返回 0 成功，1 版本不匹配
    ///     int  WINAPI Speak(const WCHAR* text, BOOL bInterrupt)
    ///                  bInterrupt: TRUE 清空排队立刻打断；FALSE 等空闲时朗读
    ///                  返回 0 成功，1 版本不匹配，2 读屏没运行或没有授权，
    ///                       8 命令没有成功送达，9 独立通道尚未就绪
    ///     int  WINAPI GetSpeakState()
    ///                  1 版本不匹配，2 没运行或没有授权，3 正在朗读，4 没有朗读，
    ///                  8 命令没有成功送达，9 独立通道尚未就绪
    ///     void WINAPI StopSpeak()
    ///     int  WINAPI Braille(const WCHAR* text, BOOL bFlashMessage)   // 1.0.0.5 起
    ///                  0 成功，1 版本不匹配，2 没运行或没有授权，3 没有连接点显器，8 没有送达
    ///
    /// === 一条必须写进代码里的教训（否则整级形同虚设）===
    ///
    /// **`Speak()` 返回 0 不代表它真的念了。** 未授权时它照样返回 0
    /// —— 那个返回值只说明消息送到了对端，与「读屏是否出声」无关。
    /// 所以本后端的可用性判据**不是** Speak 的返回值，而是
    /// `GetSpeakState()` ∈ {3,4}（3=正在朗读，4=空闲 —— 两个都说明读屏活着且有授权）。
    /// 拿不到这两个值就判「不可用」，让 Speech 继续往下试 SAPI，
    /// 而不是让玩家面对一片安静还以为是自己没配对。
    ///
    /// 商业版/青春版才有 API 权限；公益档下 GetSpeakState() 恒为 2。
    /// 这不是 bug，是授权模型 —— 本后端的处理方式是**如实判不可用**，而不是假装成功。
    ///
    /// === DLL 从哪来 ===
    ///
    /// 官方原文：
    ///
    /// > 应用程序可以直接调用争渡读屏安装目录下的相应 dll 文件，或者直接把 dll
    /// > 复制到指定的目录调用均可。……另外建议直接读取争渡读屏安装目录下的 dll 文件，
    /// > 尽量不要复制到自己程序目录。
    ///
    /// 所以**安装目录那份永远优先**（它跟着读屏一起升级）。问题在于很多玩家根本
    /// 找不到那个 dll（需求方的原话：「比如两年前的我」），于是这里做了两件事：
    ///
    ///   1. **自动找安装目录**：先读注册表里登记的安装位置
    ///      （实查：`DisplayName = 争渡读屏`、`InstallLocation = …\zdsr\zdsr`），
    ///      再回落默认目录 —— 装在 D 盘、装在自定义目录的玩家不需要做任何事；
    ///   2. **找不到就用随包带的那份兜底**：发布包里会附一份（只进发布包、不进仓库），
    ///      它排在插件目录那一档。玩家删掉它，行为就回到「只读安装目录」。
    ///
    /// 加载顺序：**注册表登记的安装目录 → 默认安装目录 → 插件目录（随包那份）→ 游戏目录**。
    ///
    /// **旧 dll 不会把玩家卡死**：某一份候选报版本不匹配（`InitTTS` 返回 1）时，
    /// 这里会自动换下一份候选再试（见 `EnsureInit` / `SelectNextCandidate`）——
    /// 随包那份随争渡升级而过期时，安装目录那份仍然会被用上；反过来也一样。
    ///
    /// 32/64 位各一个文件：ZDSRAPI.dll（32 位）/ ZDSRAPI_x64.dll（64 位），
    /// 按当前进程位数自动选。装错位宽的表现是 LoadLibrary 失败（错误码 193），
    /// 这里会把它如实写进 LastError。
    ///
    /// === 与 Speech 的分工 ===
    ///
    /// 本文件只做「能不能用 / 说什么 / 停」，**不做重试计时**（那是 Speech.Ready 的事），
    /// 也不写日志（保持可独立编译，见文件头的自律）。
    /// </summary>
    internal static class Zdsr
    {
        // ================= 对外状态 =================

        /// <summary>最近一次失败原因（人类可读）。供 Speech 拼进「为什么全灭」的结论串。</summary>
        public static string LastError = "";

        /// <summary>成功加载的 DLL 完整路径；没加载成功时为空串。</summary>
        public static string LoadedFrom = "";

        /// <summary>DLL 与导出是否已就绪（**不表示读屏在运行/有授权**，那看 <see cref="State"/>）。</summary>
        public static bool DllLoaded { get { return _lib != IntPtr.Zero; } }

        /// <summary>当前进程该用哪个文件名（按位数选）。日志里要打出来，否则「装错版本」无从判断。</summary>
        public static string DllName { get { return Environment.Is64BitProcess ? X64Name : X86Name; } }

        /// <summary>最近一次 <see cref="GetSpeakState"/> 的原始返回值；从未调用时为 -1。</summary>
        public static int State = -1;

        /// <summary>Braille 导出是否存在（1.0.0.5 起才有；旧版没有这个导出）。</summary>
        public static bool HasBrailleExport { get { return _braille != null; } }

        // ================= 内部状态 =================

        private const string X64Name = "ZDSRAPI_x64.dll";
        private const string X86Name = "ZDSRAPI.dll";

        private static readonly object _gate = new object();

        private static IntPtr _lib = IntPtr.Zero;
        private static bool _probed;          // 已经找过一遍（失败也会记住，但允许 Reset 重找）
        private static bool _initDone;        // InitTTS 是否已经成功调用过
        private static bool _initOk;
        private static int _nextRescan;       // 扫描失败后，最早什么时候再扫一次（Environment.TickCount）
        private static System.Collections.Generic.List<string> _candidates;  // 本次探测的候选目录（按优先级）
        private static int _candidateIndex;   // 当前用的是第几个候选（版本不匹配时往后走）

        /// <summary>
        /// 找不到 DLL 之后的重扫间隔。存在的理由：先开游戏后装争渡（或先开游戏、
        /// 读屏还在启动）时，不能让「第一次没找到」变成整局都找不到。
        /// 用 Environment.TickCount 而不是 UnityEngine.Time，是为了本文件能脱离 Unity 编译。
        /// </summary>
        private const int RescanMs = 20000;

        private static InitTTSDelegate _initTts;
        private static SpeakDelegate _speak;
        private static GetSpeakStateDelegate _getState;
        private static StopSpeakDelegate _stop;
        private static BrailleDelegate _braille;

        // ---- 原生导出签名。WINAPI = __stdcall（x86 上是 StdCall，x64 只有一种约定）----
        // 用委托 + GetProcAddress 而不是 [DllImport]：DllImport 的 DLL 名必须是编译期常量，
        // 而我们恰恰要**在运行时决定从哪个目录加载**，并且要求「DLL 不在」是返回值而不是异常。

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int InitTTSDelegate(int type, [MarshalAs(UnmanagedType.LPWStr)] string channelName,
                                             [MarshalAs(UnmanagedType.Bool)] bool bKeyDownInterrupt);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int SpeakDelegate([MarshalAs(UnmanagedType.LPWStr)] string text,
                                           [MarshalAs(UnmanagedType.Bool)] bool bInterrupt);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int GetSpeakStateDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void StopSpeakDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int BrailleDelegate([MarshalAs(UnmanagedType.LPWStr)] string text,
                                             [MarshalAs(UnmanagedType.Bool)] bool bFlashMessage);

        // ---- kernel32 ----

        private const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x8;

        [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibraryExW(string lpFileName, IntPtr hFile, uint dwFlags);

        [DllImport("kernel32.dll", EntryPoint = "GetProcAddress", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", EntryPoint = "FreeLibrary", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        // ================= 探测与加载 =================

        /// <summary>
        /// 找 DLL 并解析导出（幂等；失败后每 <see cref="RescanMs"/> 毫秒重扫一次）。
        /// **不**与读屏通信，所以「DLL 在但读屏没开」在这里是成功的。
        ///
        /// 版本不匹配（InitTTS 返回 1）的处理在 <see cref="EnsureInit"/>：那里会
        /// <see cref="SelectNextCandidate"/> 换下一份候选重来 —— 「包内那份太旧」不该把玩家卡死。
        /// </summary>
        public static bool Probe()
        {
            lock (_gate)
            {
                if (_lib != IntPtr.Zero) return true;
                if (_probed)
                {
                    if (unchecked(Environment.TickCount - _nextRescan) < 0) return false;
                }
                _probed = true;
                _nextRescan = unchecked(Environment.TickCount + RescanMs);

                return ProbeFrom(0);
            }
        }

        /// <summary>
        /// 从第 <paramref name="startIndex"/> 个候选开始找。<paramref name="startIndex"/>&gt;0
        /// 用在「前一份加载成功但版本不匹配」之后 —— 跳过它继续往下试。
        /// </summary>
        private static bool ProbeFrom(int startIndex)
        {
            _candidates = BuildCandidates();
            string name = DllName;
            var tried = new System.Text.StringBuilder();

            for (int i = Math.Max(0, startIndex); i < _candidates.Count; i++)
            {
                string dir = _candidates[i];
                string path;
                try { path = Path.Combine(dir, name); }
                catch { continue; }
                if (!SafeExists(path)) { continue; }

                IntPtr h = IntPtr.Zero;
                try { h = LoadLibraryExW(path, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH); }
                catch (Exception e) { LastError = "加载 " + path + " 抛异常: " + e.Message; continue; }

                if (h == IntPtr.Zero)
                {
                    int err = Marshal.GetLastWin32Error();
                    tried.Append(path).Append("(LoadLibrary 失败 ").Append(err).Append(")；");
                    continue;
                }

                if (!Resolve(h, out string missing))
                {
                    // 导出不全：不是争渡的 ZDSRAPI。释放掉，继续找下一个候选。
                    try { FreeLibrary(h); } catch { }
                    tried.Append(path).Append("(缺导出 ").Append(missing).Append(")；");
                    continue;
                }

                _lib = h;
                _candidateIndex = i;
                LoadedFrom = path;
                State = -1;
                LastError = "";
                return true;
            }

            LastError = tried.Length > 0
                ? ("没有找到可用的 " + name + "（找过：" + tried + "）")
                : ("没有找到 " + name + "（找过：争渡安装目录 / 插件目录 / 游戏目录）");
            return false;
        }

        /// <summary>
        /// 当前这份不可用（典型：版本不匹配）时换下一份候选。
        ///
        /// 为什么需要它：随包带的那份 dll 会随争渡升级而过期，而**安装目录里那份才是最新的**。
        /// 正常情况下后者排在前面、根本轮不到包里那份；但玩家机器上千奇百怪
        /// （装到别的盘、注册表里那条被清掉、装了两份……），所以「这一份报版本不匹配」
        /// 必须能自动往下试，而不是直接判死。
        /// </summary>
        private static bool SelectNextCandidate()
        {
            if (_candidates == null) return false;
            if (_candidateIndex + 1 >= _candidates.Count) return false;

            int next = _candidateIndex + 1;
            try { if (_lib != IntPtr.Zero) FreeLibrary(_lib); } catch { }
            _lib = IntPtr.Zero;
            _initTts = null; _speak = null; _getState = null; _stop = null; _braille = null;
            return ProbeFrom(next);
        }

        /// <summary>解析五个导出；核心四个缺一不可，Braille 缺了只算「没有点显器输出」。</summary>
        private static bool Resolve(IntPtr lib, out string missing)
        {
            missing = "";
            IntPtr p;

            p = GetProcAddress(lib, "InitTTS");        if (p == IntPtr.Zero) { missing = "InitTTS"; return false; }
            _initTts = (InitTTSDelegate)Marshal.GetDelegateForFunctionPointer(p, typeof(InitTTSDelegate));

            p = GetProcAddress(lib, "Speak");          if (p == IntPtr.Zero) { missing = "Speak"; return false; }
            _speak = (SpeakDelegate)Marshal.GetDelegateForFunctionPointer(p, typeof(SpeakDelegate));

            p = GetProcAddress(lib, "GetSpeakState");  if (p == IntPtr.Zero) { missing = "GetSpeakState"; return false; }
            _getState = (GetSpeakStateDelegate)Marshal.GetDelegateForFunctionPointer(p, typeof(GetSpeakStateDelegate));

            p = GetProcAddress(lib, "StopSpeak");      if (p == IntPtr.Zero) { missing = "StopSpeak"; return false; }
            _stop = (StopSpeakDelegate)Marshal.GetDelegateForFunctionPointer(p, typeof(StopSpeakDelegate));

            // 可选：1.0.0.5 起才有。1.0.0.1（本机旧装）没有这个导出，属正常情况。
            p = GetProcAddress(lib, "Braille");
            _braille = p != IntPtr.Zero
                ? (BrailleDelegate)Marshal.GetDelegateForFunctionPointer(p, typeof(BrailleDelegate))
                : null;

            return true;
        }

        /// <summary>
        /// DLL 的候选位置，按优先级：
        ///
        ///   1. **注册表里登记的实际安装目录**（`争渡读屏 → InstallLocation`）——
        ///      装在 D 盘、自定义目录的用户靠这一条自动找到，不用任何人教他们怎么找 dll；
        ///   2. 默认安装目录（`%ProgramFiles(x86)%\zdsr\zdsr`、`\zdsr\zdcloud`）；
        ///   3. 插件自己所在目录（= 随包带的那一份兜底）；
        ///   4. 游戏（宿主进程）目录。
        ///
        /// 官方建议优先用安装目录里那份（跟着读屏一起升级），所以随包那份排在它后面 ——
        /// 只有机器上真的没有可用安装目录时才会用到。
        ///
        /// `A11Y_ZDSR_DIR` 一旦设置就**只找它列出的目录**（多个用 `;` 分隔）：
        /// ① 排查用；② 自检工具靠它把搜索范围钉死，否则本机真实安装会参与进来，
        /// 断言就变成「碰运气」而不是「机械验证」。
        /// </summary>
        private static System.Collections.Generic.List<string> BuildCandidates()
        {
            var list = new System.Collections.Generic.List<string>();

            string pinned = null;
            try { pinned = Environment.GetEnvironmentVariable("A11Y_ZDSR_DIR"); } catch { }
            if (!string.IsNullOrEmpty(pinned))
            {
                foreach (string one in pinned.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string t = one.Trim().Trim('"');
                    if (t.Length > 0) list.Add(t);
                }
                return list;
            }

            // 1) 注册表里登记的实际安装目录（最可靠：覆盖非默认安装位置）
            foreach (string dir in RegistryInstallDirs()) list.Add(dir);

            // 2) 默认安装位置
            foreach (string pf in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            })
            {
                if (string.IsNullOrEmpty(pf)) continue;
                list.Add(Path.Combine(pf, "zdsr", "zdsr"));
                list.Add(Path.Combine(pf, "zdsr", "zdcloud"));
            }

            // 3) 插件目录（随包带的那份）、4) 游戏目录
            string self = SelfDir();
            if (!string.IsNullOrEmpty(self)) list.Add(self);
            string baseDir = null;
            try { baseDir = AppDomain.CurrentDomain.BaseDirectory; } catch { }
            if (!string.IsNullOrEmpty(baseDir)) list.Add(baseDir);

            // 去重（注册表那条常常与默认路径重合），保持顺序
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var uniq = new System.Collections.Generic.List<string>();
            foreach (string d in list)
            {
                if (string.IsNullOrEmpty(d)) continue;
                string key;
                try { key = Path.GetFullPath(d).TrimEnd('\\'); } catch { continue; }
                if (seen.Add(key)) uniq.Add(d);
            }
            return uniq;
        }

        /// <summary>
        /// 从注册表的「卸载」项里找争渡的安装目录。
        ///
        /// 实查依据（本机 2026-10-15）：`HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall`
        /// 下有一项 `DisplayName = 争渡读屏`、`InstallLocation = C:\Program Files (x86)\zdsr\zdsr\`。
        /// 枚举三个位置（HKLM 32 位视图 / HKLM 64 位视图 / HKCU），按 DisplayName 里是否含
        /// 「争渡」或 zdsr 判断；读不到任何东西就返回空表（**不抛异常**，注册表不该成为故障源）。
        /// </summary>
        public static System.Collections.Generic.List<string> RegistryInstallDirs()
        {
            var exact = new System.Collections.Generic.List<string>();   // DisplayName 含「争渡」= 本体
            var loose = new System.Collections.Generic.List<string>();   // 只含 zdsr = 可能是周边工具

            foreach (IntPtr hive in new[] { HKEY_LOCAL_MACHINE, HKEY_CURRENT_USER })
            {
                foreach (int view in new[] { 0, KEY_WOW64_64KEY, KEY_WOW64_32KEY })
                {
                    foreach (string names in RegSubKeyNames(hive, UninstallPath, KEY_READ | view))
                    {
                        IntPtr item = RegOpenKey(hive, UninstallPath + "\\" + names, KEY_READ | view);
                        if (item == IntPtr.Zero) continue;
                        try
                        {
                            string disp = RegGetString(item, "DisplayName");
                            if (string.IsNullOrEmpty(disp)) continue;

                            bool isProduct = disp.IndexOf("争渡", StringComparison.Ordinal) >= 0;
                            bool isLoose = disp.IndexOf("zdsr", StringComparison.OrdinalIgnoreCase) >= 0;
                            if (!isProduct && !isLoose) continue;

                            string loc = RegGetString(item, "InstallLocation");
                            if (string.IsNullOrEmpty(loc)) continue;
                            string trimmed = loc.Trim().TrimEnd('\\');
                            // 本体排前面：周边工具（例如某些挂钩工具）的目录名里也有 zdsr，
                            // 但那里没有 ZDSRAPI.dll —— 排后面只是省一次无谓的尝试
                            (isProduct ? exact : loose).Add(trimmed);
                        }
                        finally { try { RegCloseKey(item); } catch { } }
                    }
                }
            }

            exact.AddRange(loose);
            return exact;
        }

        // ---- 注册表：用 advapi32 直接 P/Invoke。
        // 不用 Microsoft.Win32.Registry：它在 netstandard2.1 的引用集里是个「转发类型」，
        // 平台层一引用就得给每个工程加 NuGet 包（Mono / IL2CPP 两边都得管）。
        // 本文件已经用同一种风格直接调 kernel32，这里保持一致，平台层依旧零额外依赖。

        private static readonly IntPtr HKEY_LOCAL_MACHINE = new IntPtr(unchecked((int)0x80000002));
        private static readonly IntPtr HKEY_CURRENT_USER = new IntPtr(unchecked((int)0x80000001));
        private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        private const int KEY_READ = 0x20019;
        private const int KEY_WOW64_64KEY = 0x0100;
        private const int KEY_WOW64_32KEY = 0x0200;
        private const int RegSz = 1;

        [DllImport("advapi32.dll", EntryPoint = "RegOpenKeyExW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int RegOpenKeyExW(IntPtr hKey, string lpSubKey, int ulOptions, int samDesired, out IntPtr phkResult);

        [DllImport("advapi32.dll", EntryPoint = "RegEnumKeyExW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int RegEnumKeyExW(IntPtr hKey, int dwIndex, System.Text.StringBuilder lpName,
                                                ref int lpcchName, IntPtr lpReserved, IntPtr lpClass,
                                                IntPtr lpcchClass, IntPtr lpftLastWriteTime);

        [DllImport("advapi32.dll", EntryPoint = "RegQueryValueExW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int RegQueryValueExW(IntPtr hKey, string lpValueName, IntPtr lpReserved,
                                                   out int lpType, byte[] lpData, ref int lpcbData);

        [DllImport("advapi32.dll", EntryPoint = "RegCloseKey", SetLastError = true)]
        private static extern int RegCloseKey(IntPtr hKey);

        private static IntPtr RegOpenKey(IntPtr hive, string path, int access)
        {
            try
            {
                IntPtr h;
                int rc = RegOpenKeyExW(hive, path, 0, access, out h);
                return rc == 0 ? h : IntPtr.Zero;
            }
            catch { return IntPtr.Zero; }
        }

        private static System.Collections.Generic.List<string> RegSubKeyNames(IntPtr hive, string path, int access)
        {
            var list = new System.Collections.Generic.List<string>();
            IntPtr key = RegOpenKey(hive, path, access);
            if (key == IntPtr.Zero) return list;
            try
            {
                var buf = new System.Text.StringBuilder(512);
                for (int i = 0; i < 4096; i++)
                {
                    int len = buf.Capacity;
                    int rc = RegEnumKeyExW(key, i, buf, ref len, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                    if (rc != 0) break;
                    list.Add(buf.ToString());
                }
            }
            catch { }
            finally { try { RegCloseKey(key); } catch { } }
            return list;
        }

        /// <summary>读一个 REG_SZ 值；读不到（不存在/类型不符/出错）一律返回 null。</summary>
        private static string RegGetString(IntPtr key, string name)
        {
            try
            {
                int type, size = 0;
                if (RegQueryValueExW(key, name, IntPtr.Zero, out type, null, ref size) != 0) return null;
                if (type != RegSz || size <= 0 || size > 8192) return null;
                var data = new byte[size];
                if (RegQueryValueExW(key, name, IntPtr.Zero, out type, data, ref size) != 0) return null;
                string s = System.Text.Encoding.Unicode.GetString(data, 0, size);
                int nul = s.IndexOf('\0');
                return (nul >= 0 ? s.Substring(0, nul) : s).Trim();
            }
            catch { return null; }
        }

        private static string SelfDir()
        {
            try
            {
                string asm = Assembly.GetExecutingAssembly() != null
                           ? Assembly.GetExecutingAssembly().Location : null;
                if (!string.IsNullOrEmpty(asm))
                {
                    string d = Path.GetDirectoryName(asm);
                    if (!string.IsNullOrEmpty(d)) return d;
                }
            }
            catch { }
            return null;
        }

        private static bool SafeExists(string path)
        {
            try { return File.Exists(path); } catch { return false; }
        }

        /// <summary>清掉探测结果，允许下一次重新找（自检工具用它模拟「DLL 后来才出现」）。</summary>
        public static void Reset()
        {
            lock (_gate)
            {
                if (_lib != IntPtr.Zero) { try { FreeLibrary(_lib); } catch { } }
                _lib = IntPtr.Zero;
                _probed = false;
                _nextRescan = 0;
                _initDone = false;
                _initOk = false;
                _nextInitTick = 0;
                _nextStateCheck = 0;
                _available = false;
                _initTts = null; _speak = null; _getState = null; _stop = null; _braille = null;
                _candidates = null;
                _candidateIndex = 0;
                LoadedFrom = "";
                State = -1;
                LastError = "";
            }
        }

        // ================= 与读屏握手 / 可用性 =================

        /// <summary>
        /// 初始化语音接口。官方文档说明可重复调用；这里只在「还没成功过」或
        /// 上一次掉线之后调一次，避免每 5 秒重试都往读屏发一次握手。
        ///
        /// 通道取 <c>0</c>（读屏通道）、<c>keyDownInterrupt = false</c>（按键不打断）：
        /// 游戏里的台词是**排队念**的（Speak 的 interrupt 参数由平台层逐句给），
        /// 若允许「按键打断」，玩家每按一下推进键都会把正在念的那句掐掉。
        /// 想改成独立通道/按键打断的玩家，可以用官方 ZDSRAPI.ini 覆盖
        /// （该 ini 的优先级高于程序调用，放在 dll 同目录或调用进程目录即可）。
        /// </summary>
        private static bool EnsureInit()
        {
            if (_initDone && _initOk) return true;

            // 失败后 5 秒内不再重复握手：读屏关着的时候，每一句台词都发一次
            // InitTTS 是白费力气（也会把日志刷满）。TestAvailable 每 10 秒会再判一次，
            // 所以「读屏中途被打开 / 加密狗插回」仍能在 10 秒内恢复。
            if (_initDone && unchecked(Environment.TickCount - _nextInitTick) < 0) return false;

            // 版本不匹配（rc=1）说明这一份 dll 与玩家机器上的争渡对不上 ——
            // 多试几份候选（安装目录那份 / 随包那份），直到有一份能用或候选用完。
            // 为什么必须这么做：随包带的那份会随争渡升级而过期，直接判死等于把玩家卡住。
            int guard = 0;
            while (true)
            {
                _initDone = true;
                _initOk = false;
                _nextInitTick = unchecked(Environment.TickCount + InitRetryMs);

                int rc;
                try { rc = _initTts(0, null, false); }
                catch (Exception e)
                {
                    LastError = "InitTTS 异常: " + e.Message;
                    return false;
                }

                if (rc == 0) { _initOk = true; return true; }

                LastError = "InitTTS 返回 " + rc + (rc == 1 ? "（版本不匹配）" : "");
                if (rc != 1) return false;                       // 只有版本不匹配才值得换一份试
                if (++guard > 8) return false;                   // 候选再多也不会无限转
                if (!SelectNextCandidate()) return false;        // 没有下一份了
            }
        }

        private static int _nextInitTick;

        /// <summary>握手失败后的重试间隔（毫秒）。</summary>
        private const int InitRetryMs = 5000;

        /// <summary>
        /// 这一级现在能不能用：DLL 在 **且** 读屏活着且有授权。
        ///
        /// 判据是 GetSpeakState() ∈ {3,4}。**不能用 Speak 的返回值判** —— 未授权时
        /// 它同样返回 0（见类注释里的教训）。
        /// </summary>
        public static bool TestAvailable()
        {
            lock (_gate)
            {
                if (!Probe()) return false;
                if (!EnsureInit()) return false;
                return CheckState(force: true);
            }
        }

        /// <summary>
        /// 状态检查（结果缓存 <see cref="StateCheckMs"/> 毫秒）。
        ///
        /// 为什么要缓存：GetSpeakState 是一次跨进程调用（本机实测 ~0.4ms 本地、
        /// ~21ms 走 IPC），而 Speak 每句台词都会被调用 —— 不能每句都查。
        /// 但也不能永远不查：读屏中途被关掉时，Speak 仍然返回 0（F-4），
        /// 只看返回值就会「一直以为自己在念，其实一片安静」且永不恢复。
        /// 5 秒是这两件事的折中：读屏回来之后最多 5 秒恢复朗读。
        /// </summary>
        private static bool CheckState(bool force)
        {
            if (!force && unchecked(Environment.TickCount - _nextStateCheck) < 0) return _available;
            _nextStateCheck = unchecked(Environment.TickCount + StateCheckMs);

            int st;
            try { st = _getState(); }
            catch (Exception e)
            {
                LastError = "GetSpeakState 异常: " + e.Message;
                _available = false;
                return false;
            }

            State = st;
            _available = (st == 3 || st == 4);
            if (_available) { LastError = ""; return true; }

            LastError = DescribeState(st);
            // 掉线了：下次重新走一遍握手（但要等门禁，否则每句台词都会重发握手）
            MarkDown();
            return false;
        }

        /// <summary>
        /// 标记「这一级现在不可用」，并让下一次握手等 <see cref="InitRetryMs"/> 毫秒。
        ///
        /// 这里有个**必须保留 `_initDone = true`** 的细节：那个字段同时充当
        /// 「重试门禁已武装」的开关。掉线时把它清成 false，下一句台词就会立刻重新握手 ——
        /// 结果是读屏关着的时候每句台词都发一次 InitTTS（自检用例 Handshake_Retry 逮到的）。
        /// </summary>
        private static void MarkDown()
        {
            _initDone = true;
            _initOk = false;
            _available = false;
            _nextInitTick = unchecked(Environment.TickCount + InitRetryMs);
        }

        private static int _nextStateCheck;
        private static bool _available;

        /// <summary>状态检查的缓存时长（毫秒）。见 <see cref="CheckState"/>。</summary>
        private const int StateCheckMs = 5000;

        /// <summary>把原始返回码翻译成人话 —— 报告和日志里都要用，别让玩家看到裸数字。</summary>
        public static string DescribeState(int st)
        {
            switch (st)
            {
                case 1: return "版本不匹配（ZDSRAPI 与读屏对不上，多半是自带的 dll 太旧）";
                case 2: return "争渡没有运行，或当前是公益档/未授权（GetSpeakState=2）";
                case 3: return "正在朗读（可用）";
                case 4: return "空闲（可用）";
                case 8: return "命令没有成功送达";
                case 9: return "独立通道尚未就绪";
                default: return "GetSpeakState 返回未知值 " + st;
            }
        }

        // ================= 朗读 =================

        /// <summary>
        /// 朗读一段文本。<paramref name="interrupt"/> = true 时清空排队立刻打断，
        /// false 时等空闲（对应官方 Speak 的 bInterrupt 参数，语义完全一致）。
        ///
        /// 返回「确实送进了活着的读屏」。判据是 `Speak()==0` **且** 状态检查说可用 ——
        /// 只有前者是不够的：未授权/读屏被关掉时 Speak() 照样返回 0（官方文档与实测
        /// 都如此），拿它当成功会让整级静默失效。失败时把 <see cref="LastError"/>
        /// 写好并清掉握手状态，读屏回来后 <see cref="TestAvailable"/> 会重新判可用。
        /// </summary>
        public static bool Speak(string text, bool interrupt)
        {
            if (string.IsNullOrEmpty(text)) return false;

            lock (_gate)
            {
                // 自己 Probe 一次（已加载时是一次空判断）：Speak 不该依赖「先有人问过可用性」，
                // 否则哪个调用点先跑就决定了它能不能出声。
                if (!Probe()) return false;
                if (!EnsureInit()) return false;

                // 先看状态：未授权时下面那次 Speak 也会返回 0，不看状态就会「假装成功」
                if (!CheckState(force: false)) return false;

                int rc;
                try { rc = _speak(text, interrupt); }
                catch (Exception e)
                {
                    LastError = "Speak 异常: " + e.Message;
                    MarkDown();
                    return false;
                }

                if (rc == 0) return true;

                LastError = DescribeSpeak(rc);
                MarkDown();
                return false;
            }
        }

        /// <summary>Speak 的返回码（与 GetSpeakState 不完全一样，分开翻译）。</summary>
        public static string DescribeSpeak(int rc)
        {
            switch (rc)
            {
                case 1: return "Speak 返回 1：版本不匹配";
                case 2: return "Speak 返回 2：争渡没有运行，或未授权";
                case 8: return "Speak 返回 8：命令没有成功送达";
                case 9: return "Speak 返回 9：独立通道尚未就绪";
                default: return "Speak 返回未知值 " + rc;
            }
        }

        /// <summary>停止朗读（对应官方 StopSpeak，无返回值）。</summary>
        public static bool Stop()
        {
            lock (_gate)
            {
                if (_lib == IntPtr.Zero || _stop == null) return false;
                try { _stop(); return true; }
                catch (Exception e) { LastError = "StopSpeak 异常: " + e.Message; return false; }
            }
        }

        // ================= 点显器（可选能力）=================

        /// <summary>
        /// 往点显器送文本。返回码里 3 = 没有连接点显器，这是**正常情况**
        /// （绝大多数玩家没有点显器），调用方据此静默停用即可，不要当天故障。
        ///
        /// 【尚未接入】0.2.0.0 只把这一级接进语音，点显器输出还没有接到
        /// Speech/Repeat 的通路上 —— 本机没有点显器，接上去的代码无法验证。
        /// 接口先放这里，等有设备的人实测后再接（诚实纪律，见流水线 §6 P4）。
        /// </summary>
        public static bool Braille(string text, bool flashMessage)
        {
            if (string.IsNullOrEmpty(text)) return false;
            lock (_gate)
            {
                if (_lib == IntPtr.Zero || _braille == null) return false;
                try { return _braille(text, flashMessage) == 0; }
                catch { return false; }
            }
        }
    }
}
