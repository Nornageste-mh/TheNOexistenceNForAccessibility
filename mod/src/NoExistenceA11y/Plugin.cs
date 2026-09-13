using System;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Naninovel;
using UnityEngine;

namespace NoExistenceA11y
{
    [BepInPlugin(Guid, "The NOexistenceN of you AND me 无障碍朗读", Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "noexistence.a11y";

        /// <summary>
        /// 纯数字，四段。**不要在版本号里加字母** —— BepInPlugin 的版本参数是
        /// System.Version，`0.1.0a` 这种会直接抛异常导致插件加载失败。
        /// </summary>
        public const string Version = "0.0.0.9";

        internal static ManualLogSource L;

        internal static ConfigEntry<bool> CfgEnabled;
        internal static ConfigEntry<bool> CfgSkipVoiced;
        internal static ConfigEntry<bool> CfgAnnounceSpeaker;
        internal static ConfigEntry<bool> CfgAnnounceOnVoiced;
        internal static ConfigEntry<bool> CfgReadTrivial;
        internal static ConfigEntry<bool> CfgRequirePrintEvent;
        internal static ConfigEntry<string> CfgSpeechBackend;
        internal static ConfigEntry<string> CfgRepeatKey;
        internal static ConfigEntry<bool> CfgSpeechInterrupt;
        internal static ConfigEntry<bool> CfgStopOnVoiced;
        internal static ConfigEntry<bool> CfgDiag;
        internal static ConfigEntry<bool> CfgAutoStart;
        internal static ConfigEntry<bool> CfgAutoPlay;
        internal static ConfigEntry<string> CfgJumpScript;
        internal static ConfigEntry<string> CfgJumpHotkey;

        // ---- 选项框 ----
        internal static ConfigEntry<bool> CfgReadChoices;
        internal static ConfigEntry<bool> CfgChoiceHotkeys;

        // ---- QTE ----
        internal static ConfigEntry<bool> CfgQteAutoPass;
        internal static ConfigEntry<string> CfgQteHotkey;
        internal static ConfigEntry<float> CfgQteFallbackDelay;

        // ---- 界面导航（UiNav）----
        internal static ConfigEntry<bool> CfgUiNav;
        internal static ConfigEntry<bool> CfgUiVisibleOnly;
        internal static ConfigEntry<bool> CfgUiSortByPosition;
        internal static ConfigEntry<string> CfgUiTextWhitelist;
        internal static ConfigEntry<bool> CfgNavBlockInput;
        internal static ConfigEntry<bool> CfgQuitConfirm;
        internal static ConfigEntry<string> CfgQuitNames;
        internal static ConfigEntry<string> CfgUiBlockedPaths;

        internal static string DiagPath;

        /// <summary>
        /// 自动化测试用：设了环境变量 NOEXISTENCE_A11Y_TEST=1 时，
        /// 等同于把「自动开始游戏」「自动推进剧情」打开。
        /// 单独走环境变量而不是去改配置文件，是因为配置键是中文，
        /// 测试脚本用 PowerShell 改中文配置会在编码上踩坑。
        /// 正常游玩永远不会碰到。
        /// </summary>
        internal static bool TestMode;

        /// <summary>
        /// 绝不能进导航的面板。
        ///
        /// · ScriptNavigatorUI —— Naninovel 的「直接跳转到任意剧本」工具。
        ///   里面有「PlayScript: Title」这类裸剧本条目，按下去会绕开正常流程
        ///   把游戏扔进裸剧本，结果是黑屏 + 有音乐 + 无任何响应，只能 Alt+F4。
        ///
        /// · SaveLoadUI —— 本作**故意**不提供存档/读档界面：游戏里只有
        ///   History 和 Options 两个按钮，主菜单只有「继续游戏」。
        ///   但 Naninovel 的存档界面整套都在（SaveToggle / LoadToggle /
        ///   QuickLoadToggle / 存档槽 / 翻页 / 删除按钮），只是被藏起来了。
        ///   实测它曾经被导航暴露成第 11、12 组（各 15 个控件）——
        ///   读到一个来路不明的存档、或在流程中途跳档，状态会错乱。
        ///   引擎自己会写自动存档（NaninovelData\Saves\1.json），玩家不需要这个界面。
        ///
        /// 想放开哪一条，把它从这个逗号分隔的列表里删掉即可。
        /// </summary>
        internal const string DefaultBlockedPaths =
            "ScriptNavigatorUI,ExternalScriptsBrowser,DebugInfoGUI,CustomVariableGUI,SaveLoadUI";

        public override void Load()
        {
            L = Log;
            try { TestMode = Environment.GetEnvironmentVariable("NOEXISTENCE_A11Y_TEST") == "1"; }
            catch { TestMode = false; }

            CfgEnabled = Config.Bind("朗读", "启用", true, "总开关。");
            CfgSkipVoiced = Config.Bind("朗读", "有配音时不再朗读", true,
                "命中配音白名单时只放游戏语音，不用 TTS 重复念一遍。");
            CfgAnnounceSpeaker = Config.Bind("朗读", "播报说话人", true,
                "朗读无配音台词前先报说话人名（如「国王：」）。旁白不报。");
            CfgAnnounceOnVoiced = Config.Bind("朗读", "有配音时也报名", true,
                "莉莉丝系有多个名号（莉莉丝/莉莉丝公主/魔王莉莉丝/神秘少女）。\n" +
                "声音一样，但屏幕上换名字是重要剧情，所以在有配音时也念一遍名字。");
            CfgReadTrivial = Config.Bind("朗读", "朗读纯省略号", false,
                "像「……」这种只有标点的行是否也读出来。默认不读。");
            CfgRequirePrintEvent = Config.Bind("朗读", "只朗读正在显示的行", true,
                "★ 剧透闸门，强烈建议保持开启。\n" +
                "RevealableText 是复用的，游戏会在面板还没显示时就把文本写进去\n" +
                "（载入存档 / 加载场景 / 预载剧本都会）。只挂 set_Text 的话，\n" +
                "这些玩家根本看不到的文本会被念出来 —— 实测在 load 场景念出过结局文本。\n" +
                "开启后只朗读同时收到「开始打印」事件的行。");
            CfgSpeechBackend = Config.Bind("朗读", "语音后端", "自动",
                "自动 / NVDA / Tolk / SAPI。填具体值可强制只用那一个（排查用）。");
            CfgRepeatKey = Config.Bind("朗读", "重读键", "Backspace",
                "按这个键重读最近朗读过的剧情文本。\n" +
                "连按可以一直往回走（最多记 30 句）；读到新的一句就回到最新。\n" +
                "记忆只在内存里，退出游戏即清空。\n" +
                "留空可停用。填 Unity 的 KeyCode 名。");
            CfgSpeechInterrupt = Config.Bind("朗读", "新台词打断上一句", false,
                "★ 默认**关**。关掉时新台词排队，等上一句念完再念 —— 长句不会被截断。\n" +
                "\n" +
                "开启的话，每来一句就取消正在念的那句。剧情推进快的时候\n" +
                "（比如自动播放）会不断把长句拦腰砍断，后半句永远听不到，\n" +
                "这是实打实的信息丢失，所以不建议开。\n" +
                "\n" +
                "排队期间如果撞上「有配音的台词」，队列仍会被清掉让位给角色语音 ——\n" +
                "否则 TTS 会和角色语音叠在一起，两边都听不清。\n" +
                "想主动清空队列的话，用上面那个「重读键」听完再继续即可。");
            CfgStopOnVoiced = Config.Bind("朗读", "撞上配音时清空待读队列", true,
                "有配音的台词会放角色语音。此时若还有上一句在排队朗读，\n" +
                "两者会叠在一起，两边都听不清 —— 所以默认为角色语音让路，清空队列。\n" +
                "\n" +
                "代价：如果上一句是长旁白还没念完，它会被砍断。\n" +
                "想优先保证「一个字都不漏」，就关掉这个开关 ——\n" +
                "代价是那一小段时间 TTS 和角色语音会重叠。");
            CfgDiag = Config.Bind("诊断", "详细日志", true, "把每一句的判定过程写进 BepInEx\\noexistence_a11y.log。");
            CfgAutoStart = Config.Bind("诊断", "自动开始游戏", false,
                "测试用：引擎就绪后自动点掉标题画面的 START，省得人手点。正式游玩保持关闭。");
            CfgAutoPlay = Config.Bind("诊断", "自动推进剧情", false,
                "测试用：开启 Naninovel 自动播放，让剧情自己往下走，不依赖任何模拟点击。正式游玩保持关闭。");
            CfgJumpScript = Config.Bind("诊断", "跳转脚本", "Prologue1_6",
                "★ 诊断功能。在游戏里按下面的热键，直接跳到这个剧本的**开头**播放。\n" +
                "用途：QTE 在第一章末尾，进度过了就够不着，没法回头验证功能。\n" +
                "从开头播 = 脚本自己的立绘装配命令都会执行，不会出现分层错乱。\n" +
                "跳过去之后按住 Ctrl（Naninovel 的跳过键）可以几秒冲到目标位置。\n" +
                "\n" +
                "烤箱 QTE 可能在 Prologue1_6，也可能在 Prologue1_1（烤蛋糕那章），\n" +
                "两个都试一下。填别的剧本名也行，比如 Prologue2_1。\n" +
                "\n" +
                "⚠️ 会打乱正常流程，而且游戏会自动存档。用之前先备份 Saves 目录。");
            CfgJumpHotkey = Config.Bind("诊断", "跳转热键", "F4",
                "触发上面那个跳转的按键。填 Unity 的 KeyCode 名，例如 F4 / F5 / BackQuote。\n" +
                "留空可停用。");

            CfgReadChoices = Config.Bind("选项框", "朗读选项", true,
                "主角的全部台词都在选项框里（全剧 ≥625 条），所以这块不是可选项。\n" +
                "每条选项出现时立刻带序号朗读。");
            CfgChoiceHotkeys = Config.Bind("选项框", "数字键直选", true,
                "选项出现时按 1-9 直接选中对应项，不必用鼠标、也不必先按 Tab。");

            CfgQteAutoPass = Config.Bind("QTE", "自动点击", true,
                "烤箱那段的 QTE 是纯视觉反应按键：按钮散落在屏幕上、超时就消失，\n" +
                "正确选项加「修正值」、错误选项点了无事发生，低于阈值要重来。\n" +
                "画面上没有任何可听的线索，盲人玩家没有可玩的通道。\n" +
                "\n" +
                "这个功能不可能让局面变坏：错误选项没有扣分，所以「把出现的按钮\n" +
                "都点一遍」在结果上严格优于「什么都不点」。采集范围也严格限制在\n" +
                "QTE 面板子树内，不会误碰别处的按钮。\n" +
                "\n" +
                "游戏里随时可以按下面的热键开关它。");
            CfgQteHotkey = Config.Bind("QTE", "开关热键", "F3",
                "在游戏里切换「自动点击」的按键。切换时会念出当前状态。\n" +
                "填 Unity 的 KeyCode 名，例如 F3 / F4 / BackQuote。");
            CfgQteFallbackDelay = Config.Bind("QTE", "兜底延迟（秒）", 1.0f,
                "★ 这就是实际生效的那个旋钮。\n" +
                "\n" +
                "修正值取决于点击时按钮的凝实程度：刚露头就点只有 50%，\n" +
                "等按钮渐显到最实再点能到 97%。所以补丁会等一会儿再点。\n" +
                "\n" +
                "实测（v0.0.0.4 日志）：按钮的渐隐不是靠 CanvasGroup / Image.color.a 做的，\n" +
                "补丁读不到 alpha 变化，所以「按峰值点」那条路 100 次里只触发了 1 次 ——\n" +
                "真正起作用的就是这个延迟。\n" +
                "1.0 秒 = 97%。想冲 100% 可以试 1.2~1.5，但调太大有漏点的风险。");

            CfgUiNav = Config.Bind("界面导航", "启用键盘导航", true,
                "按 Tab 进入/退出导航模式，方向键选项，回车/空格激活。\n" +
                "覆盖主菜单、设置、存档、选项框、标题画面等所有界面。");
            CfgUiVisibleOnly = Config.Bind("界面导航", "只导航画面上可见的控件", true,
                "关掉的话会把隐藏面板里的控件也扫进来，通常只会造成噪声。");
            CfgUiSortByPosition = Config.Bind("界面导航", "按屏幕位置排序", true,
                "关掉则按渲染层级排序。默认按位置更符合直觉。");
            CfgUiTextWhitelist = Config.Bind("界面导航", "可导航文字的界面", "BacklogUI",
                "★ 逗号分隔的路径片段。命中这些界面时，**纯文本也会变成导航项**。\n" +
                "\n" +
                "用途：回想（History）面板里每条台词是一个纯文字，没有对应的控件，\n" +
                "按原来的做法打开回想之后什么都读不到。\n" +
                "纳入之后：方向键逐条翻、回车重念当前这条 ——\n" +
                "「重读上一句」和「回想朗读」一次解决，而且用的是游戏本来就有的交互。\n" +
                "\n" +
                "按钮/开关自带的标签会自动跳过，不会重复念。\n" +
                "留空可停用。");
            CfgNavBlockInput = Config.Bind("界面导航", "导航时屏蔽游戏输入", true,
                "★ 建议保持开启。\n" +
                "回车/空格同时是「激活当前控件」和「推进剧情」。不屏蔽的话，\n" +
                "在回想面板里按回车重念一句，剧情会跟着往前走一格 ——\n" +
                "玩家只是想重听，位置却变了。\n" +
                "\n" +
                "用的是引擎自己的接口 IInputManager.ProcessInput，不是去改游戏代码：\n" +
                "进入导航时记下当时的值再关掉，退出时还原成记下的那个值，\n" +
                "所以不会覆盖游戏自己因为别的原因（比如播片）关掉的输入。");
            CfgQuitConfirm = Config.Bind("界面导航", "退出前二次确认", true,
                "「退出游戏」只按一下就关掉整个会话，而读屏用户分不清它和旁边的按钮，所以补一道确认。");
            CfgQuitNames = Config.Bind("界面导航", "退出按钮对象名", "ExitButton,QuitGame,ExitGameButton,QuitButton",
                "逗号分隔的 GameObject 名。只有名字精确命中的按钮才需要二次确认。");
            CfgUiBlockedPaths = Config.Bind("界面导航", "排除的面板路径", DefaultBlockedPaths,
                "逗号分隔的路径片段，命中就不纳入导航。\n" +
                "默认排掉的是 Naninovel 自带的**开发者工具**：\n" +
                "  ScriptNavigatorUI —— 「直接跳转到任意剧本」面板。\n" +
                "     实测踩过的坑：它能导航到「PlayScript: Title」这类原始剧本条目，\n" +
                "     按下去会绕开正常流程把游戏扔进裸剧本，结果是黑屏+有音乐+无任何响应，\n" +
                "     只能 Alt+F4。这不是游戏的问题，是我们不该让它可点。\n" +
                "  ExternalScriptsBrowser / DebugInfoGUI / CustomVariableGUI —— 同类调试面板。");

            // QTE 自动点击的运行时开关：初值取自配置，游戏里可用热键随时切换
            Qte.AutoOn = CfgQteAutoPass.Value;

            DiagPath = Path.Combine(Paths.BepInExRootPath, "noexistence_a11y.log");
            try
            {
                File.WriteAllText(DiagPath,
                    "=== NoExistence A11y v" + Version + "  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===\n",
                    new UTF8Encoding(false));
            }
            catch { }

            try
            {
                AddComponent<A11yBehaviour>();
                L.LogInfo("[a11y] behaviour added");
            }
            catch (Exception e) { L.LogError("[a11y] AddComponent failed: " + e); }

            try
            {
                var h = new Harmony(Guid);
                h.PatchAll(typeof(RevealPatch));
                h.PatchAll(typeof(ChoicePanelPatch));
                L.LogInfo("[a11y] patched RevealableText.set_Text + ChoiceHandlerPanel.AddChoiceButton");
            }
            catch (Exception e) { L.LogError("[a11y] harmony patch failed: " + e.Message); }

            L.LogInfo("[a11y] loaded");
        }

        public override bool Unload()
        {
            try { Speech.Shutdown(); } catch { }
            return true;
        }

        // ==================== 诊断 ====================

        internal static void Diag(string s)
        {
            if (CfgDiag != null && !CfgDiag.Value) return;
            try
            {
                File.AppendAllText(DiagPath,
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + "\n",
                    new UTF8Encoding(false));
            }
            catch { }
        }

        // ==================== 待处理行（由打印事件填，由 set_Text 消费）====================

        internal sealed class Pending
        {
            public string Author;
            public string Reference;
            public string LineKey;
            public string PrinterId;
            public float At;
        }

        internal static Pending Current;

        internal static void OnPrintStarted(PrintTextArgs args)
        {
            string author = null, reference = null, printerId = null;
            try { author = args.AuthorId; } catch { }
            try { reference = args.Text == null ? null : args.Text.ToString(); } catch { }
            try
            {
                // ITextPrinterActor 是扁平化接口，不带 IActor.Id，必须 TryCast 一次
                if (args.Printer != null)
                {
                    var actor = args.Printer.TryCast<Naninovel.IActor>();
                    if (actor != null) printerId = actor.Id;
                }
            }
            catch { }

            if (printerId != null && printerId.ToLowerInvariant().Contains("preview"))
            {
                Diag("EVENT  skip printer=" + printerId);
                return;
            }

            Current = new Pending
            {
                Author = author,
                Reference = reference,
                LineKey = TextProc.ExtractLineKey(reference),
                PrinterId = printerId,
                At = Time.realtimeSinceStartup,
            };
            Diag(string.Format("EVENT  author={0} printer={1} key={2} ref={3}",
                author ?? "(空)", printerId ?? "?", Current.LineKey ?? "-", Short(reference)));
        }

        private static string Short(string s)
        {
            if (s == null) return "-";
            s = s.Replace("\n", "\\n");
            return s.Length <= 60 ? s : s.Substring(0, 60) + "…";
        }

        // ==================== 重读上一句 ====================

        /// <summary>
        /// 最近朗读过的剧情文本。**只在内存里，不落盘**（玩家要求的「非持久化记忆」）。
        /// 按一次退格 = 重读最后一条；再按 = 继续往回走；读到新的一句就回到最新。
        /// 有配音的行也会记进来 —— 语音错过了正需要重读文本。
        /// </summary>
        private static readonly System.Collections.Generic.List<string> _recent =
            new System.Collections.Generic.List<string>();
        private static int _back;
        private const int RecentMax = 30;

        internal static void Remember(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            _back = 0;                            // 有新内容就回到最新
            if (_recent.Count > 0 && _recent[_recent.Count - 1] == line) return;
            _recent.Add(line);
            if (_recent.Count > RecentMax) _recent.RemoveAt(0);
        }

        internal static void RepeatBack()
        {
            if (_recent.Count == 0) { Speech.Speak("还没有朗读过内容。", true); return; }
            if (_back >= _recent.Count) { _back = _recent.Count; Speech.Speak("再往前没有了。", true); return; }

            string line = _recent[_recent.Count - 1 - _back];
            _back++;
            Diag("重读 往回第 " + _back + " 句: " + Short(line));
            Speech.Speak(line, true);
        }

        internal static void CheckRepeatHotkey()
        {
            KeyCode k;
            try
            {
                string s = CfgRepeatKey != null ? CfgRepeatKey.Value : "Backspace";
                if (string.IsNullOrEmpty(s)) return;
                k = (KeyCode)Enum.Parse(typeof(KeyCode), s.Trim(), true);
            }
            catch { return; }
            if (k == KeyCode.None) return;

            bool down;
            try { down = Input.GetKeyDown(k); } catch { return; }
            if (down) RepeatBack();
        }

        // ==================== set_Text 触发 ====================

        private static string _lastPrint = "";
        private static float _lastAt;

        // 同一个打印面板的文本是「累加」的：章节标题面板先显示标题，再把引言接在后面。
        // 只跟踪整段文本的话，第二行会把标题重念一遍。所以按面板记全文，只朗读新增的那段。
        private static readonly System.Collections.Generic.Dictionary<int, string> _panelText =
            new System.Collections.Generic.Dictionary<int, string>();

        internal static void OnReveal(Naninovel.UI.RevealableText inst, string raw)
        {
            if (CfgEnabled == null || !CfgEnabled.Value) return;
            if (string.IsNullOrEmpty(raw)) return;

            // ---- 只取本面板新增的部分 ----
            string full = raw;
            int panelId = 0;
            try { if (inst != null) panelId = inst.GetInstanceID(); } catch { }
            if (panelId != 0)
            {
                string prev;
                if (_panelText.TryGetValue(panelId, out prev) && !string.IsNullOrEmpty(prev))
                {
                    if (full == prev) return;                        // 完全没变
                    if (full.Length > prev.Length && full.StartsWith(prev))
                        raw = full.Substring(prev.Length);            // 只读新增
                }
                _panelText[panelId] = full;
            }

            string speech = TextProc.ToSpeech(raw);
            if (string.IsNullOrEmpty(speech)) return;

            string fp = TextProc.Fingerprint(speech);
            float now = Time.realtimeSinceStartup;

            if (fp.Length > 0 && Time.realtimeSinceStartup - _lastAt < 6f && fp == _lastPrint)
            {
                Diag("REVEAL dup  " + Short(speech));
                return;
            }
            _lastPrint = fp;
            _lastAt = now;

            var p = Current;
            bool fresh = p != null && (Time.realtimeSinceStartup - p.At) < 3f;

            string author = fresh ? p.Author : null;
            string key = fresh ? p.LineKey : null;
            string printer = fresh ? p.PrinterId : null;

            // ★ 剧透闸门。
            //
            // RevealableText 是复用/预实例化的，Naninovel 会在**面板还没显示**的时候
            // 就把文本写进去（载入存档、加载场景、预载下一段剧本都会）。
            // 只挂 set_Text 的话，这些「玩家现在根本看不到」的文本会被念出来 ——
            // 实测在 load 场景直接念出了结局文本和后面的剧情。这是不能接受的。
            //
            // 唯一的权威信号是 OnPrintTextStarted：它只在真正开始打印一行时触发。
            // 没有它陪同的 set_Text，一律不念。
            if (!fresh && (CfgRequirePrintEvent == null || CfgRequirePrintEvent.Value))
            {
                Diag("REVEAL 拦下（没有打印事件，不是正在显示的行）  " + Short(speech));
                return;
            }

            // ★ 第二道闸门：面板此刻是不是真的在画面上。
            //
            // 光有「打印事件」还不够 —— Naninovel 的面板用 CanvasGroup 淡入淡出，
            // 藏起来时 GameObject 仍是 active。实测仍有不属于当前剧情的文本被念出来，
            // 就是漏在这一层。这里查的是承载文本的那个 RevealableText 自己的可见性。
            if (inst != null && UiVis.Hidden(inst.transform))
            {
                Diag("REVEAL 拦下（面板不可见，alpha=0 或已禁用）  " + Short(speech));
                return;
            }

            bool voiced = key != null && VoiceLineIds.Voiced.Contains(key);
            string name = SpeakerNames.Resolve(author);

            Diag(string.Format("REVEAL fresh={0} printer={1} author={2} name={3} key={4} voiced={5} text={6}",
                fresh, printer ?? "-", author ?? "(空)", name ?? "-", key ?? "-", voiced, Short(speech)));

            if (p != null) Current = null;   // 一行只消费一次

            if (!CfgReadTrivial.Value && TextProc.IsTrivial(speech)) return;

            bool narr = string.IsNullOrEmpty(author);

            string line = narr || !CfgAnnounceSpeaker.Value ? speech : (name + "：" + speech);

            // 有配音的行也记进来 —— 语音错过了，退格重读文本正是玩家要的
            Remember(line);

            if (voiced)
            {
                // 有游戏语音：默认掐掉还在排队的上一句，避免和角色语音叠在一起
                // （叠起来两边都听不清）。但这也会把「上一句长旁白」砍断 ——
                // 想优先保证一字不漏，就把下面那个开关关掉。
                if (CfgStopOnVoiced == null || CfgStopOnVoiced.Value) Speech.Stop();

                if (!narr && CfgAnnounceSpeaker.Value && CfgAnnounceOnVoiced.Value
                    && author != "Lilith" && author != "Lilith_1")
                    Speech.Speak(name, false);
                return;
            }

            // 默认不打断：新台词排队，等上一句念完。长句被截断是实打实的信息丢失。
            Speech.Speak(line, CfgSpeechInterrupt != null && CfgSpeechInterrupt.Value);
        }
    }

    /// <summary>订阅 Naninovel 的打印事件，拿到 AuthorId 与行号引用。</summary>
    public class A11yBehaviour : MonoBehaviour
    {
        private bool _speechInited;
        private bool _subscribed;
        private int _ticks;

        public void Update()
        {
            _ticks++;

            // 界面导航：keep-submit-off 必须无条件每帧重申（新场景的 EventSystem 默认又是 true）
            try { UiNav.KeepUnitySubmitOff(); } catch { }
            if (Plugin.CfgUiNav == null || Plugin.CfgUiNav.Value)
            {
                try { UiNav.Update(); } catch (Exception e) { Plugin.Diag("UiNav: " + e.Message); }
            }
            try { Choices.Update(); } catch (Exception e) { Plugin.Diag("Choices: " + e.Message); }
            try { Plugin.CheckRepeatHotkey(); } catch (Exception e) { Plugin.Diag("Repeat: " + e.Message); }
            try { Qte.Update(); } catch (Exception e) { Plugin.Diag("Qte: " + e.Message); }
            try { Diag.Update(); } catch (Exception e) { Plugin.Diag("Diag: " + e.Message); }

            if (!_speechInited)
            {
                _speechInited = true;
                try
                {
                    Speech.Init(Plugin.L);
                    Plugin.L.LogInfo("[a11y] 语音后端: " + Speech.BackendName);
                    Plugin.Diag("speech backend = " + Speech.BackendName);
                }
                catch (Exception e) { Plugin.L.LogError("[a11y] Speech.Init: " + e.Message); }
            }

            if (!_subscribed)
            {
                try
                {
                    if (!Naninovel.Engine.Initialized) return;
                    var mgr = Naninovel.Engine.GetService<ITextPrinterManager>();
                    if (mgr == null) return;

                    Action<PrintTextArgs> handler = Plugin.OnPrintStarted;
                    var del = Il2CppInterop.Runtime.DelegateSupport
                        .ConvertDelegate<Il2CppSystem.Action<PrintTextArgs>>(handler);
                    mgr.add_OnPrintTextStarted(del);

                    _subscribed = true;
                    Plugin.L.LogInfo("[a11y] SUBSCRIBED to OnPrintTextStarted");
                    Plugin.Diag("subscribed to OnPrintTextStarted");
                    _subscribedAt = _ticks;
                }
                catch (Exception e)
                {
                    if (_ticks % 300 == 0)
                        Plugin.L.LogWarning("[a11y] 等待引擎: " + e.GetType().Name + " " + e.Message);
                }
            }

            if (_subscribed && !_autoStartTried && _ticks - _subscribedAt > 300)
            {
                _autoStartTried = true;
                if (Plugin.TestMode || (Plugin.CfgAutoStart != null && Plugin.CfgAutoStart.Value))
                    TryAutoStart();
            }

            // 测试用：让剧情自己走，不依赖模拟点击（模拟点击会被 Windows 的前台窗口锁拦住）
            if (_subscribed && !_autoPlayOn && _ticks - _subscribedAt > 400
                && (Plugin.TestMode || (Plugin.CfgAutoPlay != null && Plugin.CfgAutoPlay.Value)))
            {
                _autoPlayOn = true;
                try
                {
                    var sp = Naninovel.Engine.GetService<IScriptPlayer>();
                    if (sp != null)
                    {
                        sp.SetAutoPlayEnabled(true);
                        Plugin.L.LogInfo("[a11y] 自动播放已开启");
                        Plugin.Diag("autoplay ON");
                    }
                }
                catch (Exception e) { Plugin.Diag("autoplay failed: " + e.Message); }
            }
        }

        private bool _autoPlayOn;

        private int _subscribedAt;
        private bool _autoStartTried;

        /// <summary>诊断用：自动点掉标题画面的 START。</summary>
        private void TryAutoStart()
        {
            string[] names = { "CustomEnterGameButton", "TitleNewGameButton", "StartGame", "NewGameButton" };
            foreach (var n in names)
            {
                try
                {
                    var go = UnityEngine.GameObject.Find(n);
                    if (go == null) continue;
                    var btn = go.GetComponent<UnityEngine.UI.Button>();
                    if (btn == null) continue;
                    btn.onClick.Invoke();
                    Plugin.L.LogInfo("[a11y] auto-start: clicked " + n);
                    Plugin.Diag("auto-start clicked " + n);
                    return;
                }
                catch { }
            }
            // 兜底：把场景里所有按钮名记下来，下一轮好定位
            try
            {
                var all = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.UI.Button>();
                var sb = new System.Text.StringBuilder("auto-start: 候选按钮 ");
                int c = 0;
                foreach (var b in all)
                {
                    if (b == null || b.gameObject == null) continue;
                    sb.Append(b.gameObject.name).Append(" | ");
                    if (++c >= 30) break;
                }
                Plugin.Diag(sb.ToString());
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(Naninovel.UI.RevealableText), "set_Text")]
    public static class RevealPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Naninovel.UI.RevealableText __instance, string value)
            => Plugin.OnReveal(__instance, value);
    }
}
