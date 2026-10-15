using System;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Logging;

namespace NoExistenceA11y
{
    /// <summary>
    /// 语音输出抽象层。按可用性依次挑选后端：
    ///
    ///   1. Tolk          —— 若 Tolk.dll 存在。一套接口覆盖
    ///                       NVDA / JAWS / SuperNova / ZoomText / System Access，
    ///                       并可用 SAPI 兜底，同时支持语音与盲文输出。
    ///   2. NVDA          —— nvdaControllerClient.dll，本项目实测可用。
    ///   3. 争渡读屏      —— ZDSRAPI（见 Zdsr.cs），国内使用最广的读屏；
    ///                       判据是 GetSpeakState() ∈ {3,4}，**不是** Speak 的返回值。
    ///   4. SAPI          —— Windows 内置语音，无需任何读屏软件。
    ///
    /// 之所以自带 NVDA / 争渡 / SAPI 三级而不强依赖 Tolk：Tolk 官方不提供预编译
    /// 二进制，只有 MSVC 工程；而在只装了 NVDA 的机器上，JAWS / SuperNova /
    /// ZoomText 等分支根本无法验证。与其塞一份没测过的互操作代码，不如让
    /// 每一级都可独立工作，Tolk 作为「有则更好」的增强。
    ///
    /// 把 Tolk.dll 及 Tolk 的依赖 DLL（nvdaControllerClient64.dll、SAAPI64.dll）
    /// 放进 BepInEx\plugins\ 即可自动启用。
    ///
    /// 争渡这一级**不需要用户放任何文件**：按官方建议直接从争渡安装目录加载
    /// ZDSRAPI（32/64 位各一个文件名，按进程位数选），那份还会跟着读屏一起升级。
    /// 详见 Zdsr.cs 的类注释。
    /// </summary>
    internal static class Speech
    {
        public enum Backend { None, Tolk, Nvda, Zdsr, Sapi }

        private static Backend _backend = Backend.None;
        private static bool _inited;
        public static string LastError = "";

        public static Backend Current { get { return _backend; } }

        public static string BackendName
        {
            get
            {
                switch (_backend)
                {
                    case Backend.Tolk: return _tolkReader != null ? ("Tolk (" + _tolkReader + ")") : "Tolk";
                    case Backend.Nvda: return "NVDA";
                    case Backend.Zdsr: return "争渡读屏";
                    case Backend.Sapi: return "SAPI";
                    default: return "无";
                }
            }
        }

        // ================= 初始化 =================

        /// <summary>默认优先级：NVDA → Tolk → 争渡 → SAPI（NVDA 完全体优先，Tolk 白送其它读屏）。</summary>
        private static readonly Backend[] Default =
            { Backend.Nvda, Backend.Tolk, Backend.Zdsr, Backend.Sapi };

        /// <summary>
        /// 后端优先级。配置「语音后端」填了具体值时只试那一个（排查用）。
        /// log 为 null 时不打印配置写错的告警（重试路径上会刷屏）。
        /// </summary>
        private static Backend[] Order(ManualLogSource log)
        {
            // 运行中切换后端时，探测阶段用一个临时的钉死值（不落盘），
            // 免得每试一档都往配置文件里写一次。
            string cfg = _probeOverride;
            if (cfg == null)
                cfg = Plugin.CfgSpeechBackend != null ? Plugin.CfgSpeechBackend.Value : "";
            string t = cfg != null ? cfg.Trim() : "";
            if (t.Length == 0) return Default;

            string u = t.ToUpperInvariant();
            if (u == "自动" || u == "AUTO") return Default;

            Backend forced;
            if (ParseBackend(u, out forced)) return new[] { forced };

            if (log != null)
                log.LogWarning("配置「语音后端」的值认不出来：" + t
                    + "（可用：自动 / Tolk / NVDA / 争渡 / SAPI），已按「自动」处理。");
            return Default;
        }

        private static bool ParseBackend(string upper, out Backend b)
        {
            b = Backend.None;
            if (upper == null) return false;
            if (upper.Contains("TOLK")) { b = Backend.Tolk; return true; }
            if (upper.Contains("NVDA")) { b = Backend.Nvda; return true; }
            // 中英文都认：「争渡」「ZDSR」写法在玩家两边都出现过
            if (upper.Contains("ZDSR") || upper.Contains("争渡")) { b = Backend.Zdsr; return true; }
            if (upper.Contains("SAPI")) { b = Backend.Sapi; return true; }
            return false;
        }

        private static bool TryBackend(Backend b, ManualLogSource log)
        {
            switch (b)
            {
                case Backend.Tolk: return TryTolk(log);
                case Backend.Nvda: return TryNvda(log);
                case Backend.Zdsr: return TryZdsr(log);
                case Backend.Sapi: return TrySapi(log);
            }
            return false;
        }

        /// <summary>把每个后端的结论串起来，日志里一眼能看出「为什么全灭」。</summary>
        private static string Verdict(Backend b, string why)
        {
            string name;
            switch (b)
            {
                case Backend.Tolk: name = "Tolk"; break;
                case Backend.Nvda: name = "NVDA"; break;
                case Backend.Zdsr: name = "争渡"; break;
                case Backend.Sapi: name = "SAPI"; break;
                default: name = "?"; break;
            }
            return name + "：" + (string.IsNullOrEmpty(why) ? "不可用" : why) + "；";
        }

        public static void Init(ManualLogSource log)
        {
            if (_inited) return;
            _inited = true;

            var tried = new System.Text.StringBuilder();
            foreach (Backend b in Order(log))
            {
                LastError = "";
                if (!TryBackend(b, log)) { tried.Append(Verdict(b, LastError)); continue; }
                _backend = b;
                return;
            }

            _backend = Backend.None;
            if (log != null) log.LogWarning("没有可用的语音后端。逐个结论：" + tried);
        }

        // ================= 运行中切换后端 =================

        /// <summary>
        /// 可切换的档位，数组顺序就是循环顺序。第一档是「自动」（= 按平台层默认优先级挑）。
        ///
        /// 为什么把「自动」也算一档：玩家把后端钉到某一档之后，得有一条路回到默认行为，
        /// 否则只能去改配置文件。循环一圈会回到它。
        /// </summary>
        private static readonly string[] CycleOrder = { "自动", "Tolk", "NVDA", "争渡", "SAPI" };

        /// <summary>切换后端时的临时钉死值（null = 按配置文件来）。见 <see cref="CycleAvailable"/>。</summary>
        private static string _probeOverride;

        /// <summary>
        /// 切到下一个「当前可用」的后端，写回配置文件（下次启动仍是它），
        /// 返回**要播报的文本**；调用方负责念出来。
        ///
        /// 为什么由调用方念、而不是这里直接说：平台层的朗读出口只有一个
        /// （逐作层走 Repeat，见 lint_repeat.ps1），这里自己发声会绕过重读缓冲区 ——
        /// 那个坑在四个仓库里踩过两次。
        ///
        /// 语义：
        ///   · 只切到**探测得到**的后端；探测不到的跳过，并在播报里说明跳过了谁。
        ///   · 探测用的临时值不落盘，只有最终选中的那一档才写进配置。
        ///   · 一档都不可用时回到「自动」，播报里列出被跳过的档位。
        ///
        /// 「自动」这一档有一条额外的空转规则（不写清楚就会变成一个看起来坏掉的键）：
        /// 如果**当前配置本来就是「自动」**，那么再切到「自动」等于什么都没变 ——
        /// 直接跳过它继续往前找。否则「只有一个后端可用、且它正好是最后一档」时，
        /// 按一下键只会从自动跳到自动，玩家会以为这个键坏了（宿主集成测试里推演到过）。
        /// 反过来，从**钉死**的档位按下去时，「自动」是有效的落点（= 取消钉死）。
        /// </summary>
        public static string CycleAvailable()
        {
            bool wasAuto = IsAutoConfig();
            int idx = CycleIndexOf(ConfigText());
            var skipped = new System.Collections.Generic.List<string>();

            for (int step = 1; step <= CycleOrder.Length; step++)
            {
                int k = (idx + step) % CycleOrder.Length;
                string name = CycleOrder[k];

                if (name == "自动")
                {
                    if (wasAuto) continue;      // 已经在自动上：这一档是空转，继续往下找
                    Commit("自动");
                    return Announce("自动", skipped);
                }

                if (TryPin(name))
                {
                    Commit(name);
                    return Announce(BackendName, skipped);
                }
                skipped.Add(name);
            }

            Commit("自动");
            return "语音后端：自动（" + string.Join("、", skipped) + " 都不可用）";
        }

        /// <summary>拼播报文本：落了哪一档 + 路上跳过了谁（跳过的必须念出来，见方法注释）。</summary>
        private static string Announce(string name, System.Collections.Generic.List<string> skipped)
        {
            return skipped.Count == 0
                ? ("语音后端：" + name)
                : ("语音后端：" + name + "（已跳过 " + string.Join("、", skipped) + "）");
        }

        /// <summary>配置当前是不是「自动」（空值也按自动算）。</summary>
        private static bool IsAutoConfig()
        {
            string t = ConfigText();
            if (t == null) return true;
            t = t.Trim();
            if (t.Length == 0) return true;
            string u = t.ToUpperInvariant();
            return u == "自动" || u == "AUTO";
        }

        /// <summary>把某一档当「钉死」试一次，看它能不能真的起来。不落盘、不写日志。</summary>
        private static bool TryPin(string name)
        {
            _probeOverride = name;
            _inited = false;
            _backend = Backend.None;
            try { Init(null); }
            finally { _probeOverride = null; }
            return _backend != Backend.None;
        }

        /// <summary>把选中的档位写回配置（ConfigEntry 赋值会落盘），并按新配置重新选一次。</summary>
        private static void Commit(string name)
        {
            try { if (Plugin.CfgSpeechBackend != null) Plugin.CfgSpeechBackend.Value = name; }
            catch { }
            _inited = false;
            _backend = Backend.None;
            Init(Plugin.L);
        }

        private static string ConfigText()
        {
            try
            {
                return Plugin.CfgSpeechBackend != null ? (Plugin.CfgSpeechBackend.Value ?? "") : "";
            }
            catch { return ""; }
        }

        /// <summary>
        /// 当前配置对应循环里的第几档。
        ///
        /// 「自动」这一档要特别处理：它不是一个具体后端，而是「按默认优先级挑一个」，
        /// 所以它的位置要按**实际选中的那个后端**算 —— 否则第一下按下去切到的还是同一个
        /// （实测：开着 NVDA、配置为「自动」时，自动挑中的就是 NVDA），玩家会以为
        /// 这个功能没生效。按实际后端算之后，第一下就落到下一个（争渡）。
        /// </summary>
        private static int CycleIndexOf(string text)
        {
            string t = text != null ? text.Trim() : "";
            string u = t.ToUpperInvariant();

            if (t.Length == 0 || u == "自动" || u == "AUTO")
            {
                switch (_backend)
                {
                    case Backend.Tolk: return 1;
                    case Backend.Nvda: return 2;
                    case Backend.Zdsr: return 3;
                    case Backend.Sapi: return 4;
                    default: return 0;      // 一个都没起来：从「Tolk」那一位开始往下试
                }
            }

            if (u.Contains("TOLK")) return 1;
            if (u.Contains("NVDA")) return 2;
            if (u.Contains("ZDSR") || u.Contains("争渡")) return 3;
            if (u.Contains("SAPI")) return 4;
            return 0;
        }

        /// <summary>定期重试：NVDA 可能后启动，Tolk 也可能中途可用。</summary>
        public static bool Ready()
        {
            if (_backend != Backend.None) return true;
            if (UnityEngine.Time.realtimeSinceStartup < _nextRetry) return false;
            _nextRetry = UnityEngine.Time.realtimeSinceStartup + RetryInterval;

            _inited = false;
            Init(Plugin.L);
            return _backend != Backend.None;
        }

        private static float _nextRetry;
        private const float RetryInterval = 10f;

        // ================= Tolk =================

        private const string TolkDll = "Tolk.dll";

        [DllImport(TolkDll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Tolk_Load();

        [DllImport(TolkDll, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool Tolk_IsLoaded();

        [DllImport(TolkDll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Tolk_Unload();

        [DllImport(TolkDll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void Tolk_TrySAPI([MarshalAs(UnmanagedType.I1)] bool trySAPI);

        [DllImport(TolkDll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern IntPtr Tolk_DetectScreenReader();

        [DllImport(TolkDll, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool Tolk_HasSpeech();

        [DllImport(TolkDll, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool Tolk_HasBraille();

        [DllImport(TolkDll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool Tolk_Output(string str, [MarshalAs(UnmanagedType.I1)] bool interrupt);

        [DllImport(TolkDll, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool Tolk_Silence();

        private static bool _tolkLoaded;
        private static string _tolkReader;
        private static bool _tolkHasBraille;

        public static bool TolkHasBraille { get { return _tolkHasBraille; } }

        private static bool TryTolk(ManualLogSource log)
        {
            try
            {
                Tolk_TrySAPI(true);   // 无读屏时用 SAPI 兜底
                Tolk_Load();
                if (!Tolk_IsLoaded()) return false;

                _tolkLoaded = true;

                IntPtr p = Tolk_DetectScreenReader();
                _tolkReader = p != IntPtr.Zero ? Marshal.PtrToStringUni(p) : null;
                _tolkHasBraille = Tolk_HasBraille();

                if (log != null) log.LogInfo("语音后端: Tolk，读屏=" + (_tolkReader ?? "(SAPI 兜底)")
                    + (_tolkHasBraille ? "，支持盲文" : ""));
                return true;
            }
            catch (Exception e)
            {
                LastError = "Tolk 不可用: " + e.Message;
                return false;
            }
        }

        // ================= NVDA =================

        private static bool TryNvda(ManualLogSource log)
        {
            try
            {
                if (!Nvda.DllOk) Nvda.Probe();
                if (!Nvda.DllOk)
                {
                    LastError = Nvda.LastError;
                    return false;
                }
                if (!Nvda.TestRunning())
                {
                    LastError = Nvda.LastError;
                    return false;
                }
                if (log != null) log.LogInfo("语音后端: NVDA");
                return true;
            }
            catch (Exception e)
            {
                LastError = "NVDA 不可用: " + e.Message;
                return false;
            }
        }

        // ================= 争渡读屏（ZDSR）=================

        /// <summary>
        /// 交给 Zdsr.cs（运行时加载 ZDSRAPI + 可用性判据）。
        ///
        /// **判据是 GetSpeakState() ∈ {3,4}，不是 Speak() 的返回值** ——
        /// 未授权时 Speak() 照样返回 0（官方接口文档与实测都如此），
        /// 若拿它当判据，玩家会得到「选了争渡、日志一片绿、耳朵一片安静」。
        /// </summary>
        private static bool TryZdsr(ManualLogSource log)
        {
            try
            {
                if (!Zdsr.TestAvailable())
                {
                    LastError = Zdsr.LastError;
                    return false;
                }
                if (log != null) log.LogInfo("语音后端: 争渡读屏（ZDSR），DLL=" + Zdsr.LoadedFrom
                    + "，状态=" + Zdsr.DescribeState(Zdsr.State));
                _zdsrWarned = false;
                return true;
            }
            catch (Exception e)
            {
                LastError = "争渡不可用: " + e.GetType().Name + ": " + e.Message;
                return false;
            }
        }

        /// <summary>争渡掉线时只报一次，避免每句台词刷一行日志。</summary>
        private static bool _zdsrWarned;

        // ================= SAPI =================

        /// <summary>
        /// 交给 Sapi.cs（纯 P/Invoke + vtable）。
        ///
        /// v0.5.x 这里走的是 `Type.GetTypeFromProgID` + `InvokeMember` 的 COM 后期
        /// 绑定，而 Unity 的 Mono **没有实现**它 —— 玩家机器上的日志：
        ///     SAPI 不可用: NotImplementedException: The method or operation is not implemented.
        /// 也就是说「SAPI 兜底」从来没出过声：只装了争渡（或什么读屏都没装）的
        /// 机器上后端全灭，表现就是整局游戏一片安静。v0.5.8a 修复。
        /// </summary>
        private static bool TrySapi(ManualLogSource log)
        {
            try
            {
                if (!Sapi.TryInit(log)) { LastError = Sapi.LastError; return false; }
                return true;
            }
            catch (Exception e)
            {
                LastError = "SAPI 不可用: " + e.GetType().Name + ": " + e.Message;
                return false;
            }
        }

        // ================= 对外接口 =================

        public static void Speak(string text, bool interrupt)
        {
            if (string.IsNullOrEmpty(text)) return;

            // 后端不可用时定期重试（NVDA 可能后启动，或读屏中途被打开）
            if (_backend == Backend.None && !Ready()) return;

            try
            {
                switch (_backend)
                {
                    case Backend.Tolk:
                        // Tolk_Output 同时走语音与盲文
                        Tolk_Output(text, interrupt);
                        break;

                    case Backend.Nvda:
                        Nvda.Speak(text, interrupt);
                        break;

                    case Backend.Zdsr:
                        // 失败时 Speech 这一层只记一次日志：争渡可能只是被关掉了，
                        // Ready()/Init 的重试路径每 10 秒会重新判一次可用性。
                        if (Zdsr.Speak(text, interrupt)) { _zdsrWarned = false; }
                        else if (!_zdsrWarned)
                        {
                            _zdsrWarned = true;
                            LastError = Zdsr.LastError;
                            if (Plugin.L != null)
                                Plugin.L.LogWarning("争渡后端朗读失败：" + Zdsr.LastError
                                    + "（会每 10 秒重试；期间可能没有语音）");
                        }
                        break;

                    case Backend.Sapi:
                        Sapi.Speak(text, interrupt);
                        break;
                }
            }
            catch (Exception e)
            {
                LastError = "朗读失败: " + e.Message;
                _backend = Backend.None;
                _inited = false;
            }
        }

        public static void Stop()
        {
            if (_backend == Backend.None) return;
            try
            {
                switch (_backend)
                {
                    case Backend.Tolk:
                        Tolk_Silence();
                        break;
                    case Backend.Nvda:
                        Nvda.Stop();
                        break;
                    case Backend.Zdsr:
                        Zdsr.Stop();
                        break;
                    case Backend.Sapi:
                        Sapi.Stop();
                        break;
                }
            }
            catch
            {
                _backend = Backend.None;
                _inited = false;
            }
        }

        public static void Shutdown()
        {
            try { if (_tolkLoaded) Tolk_Unload(); } catch { }
            _tolkLoaded = false;
            try { Sapi.Shutdown(); } catch { }
        }
    }
}
