using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NoExistenceA11y
{
    /// <summary>
    /// QTE（烤箱那段）辅助。
    ///
    /// === 实机确认的形态（玩家描述 + 实测）===
    ///   屏幕上散落着大小不一的选项按钮。每个按钮的生命是：
    ///     **透明 → 渐显到最凝实 → 保持 → 渐隐消失**
    ///   「修正值」取决于**点击那一刻按钮的凝实程度**：
    ///     · 最凝实的时候点 → 高修正值
    ///     · 刚露头就点     → 最低档
    ///   正确选项加分，错误选项点了无事发生，低于阈值要重来。
    ///
    /// === v0.0.0.2 的错误 ===
    ///   那一版是「按钮一变成激活就立刻点」—— 正好落在最不凝实的瞬间。
    ///   玩家实测结果：**刚好 50%，刚好够推进剧情**。功能没错，时机全错。
    ///
    /// === 点击时机（自动通过开启时）===
    ///   每个按钮跟一个状态，逐帧读它的不透明度：
    ///     · 记录见过的最大 alpha（峰值）
    ///     · 一旦 alpha 从峰值**开始回落**，说明刚过最凝实的时刻 → 点
    ///
    ///   ⚠️ 实测结论（v0.0.0.4 的日志）：**真正起作用的是「兜底延迟」，不是峰值。**
    ///      点击日志里 100 次有 99 次标的是「兜底超时」，alpha 栏几乎恒为 1.00 ——
    ///      说明这个游戏按钮的渐隐**不是**靠 CanvasGroup 或 Image.color.a 做的，
    ///      峰值路径基本没触发过。玩家的 50% -> 97% 完全来自「等 1 秒再点」。
    ///
    ///      所以调参请调配置里的「兜底延迟（秒）」，不要指望改 alpha 的读法。
    ///      峰值这段保留着是因为它无害（万一某个按钮真的用 CanvasGroup 渐显，
    ///      它就能吃到峰值），但**不要把它当成主力机制**。
    ///
    ///      想冲 100% 可以把兜底延迟调到 1.2~1.5 试试；97% 已经远超阈值。
    ///
    ///   两条兜底，防止读不到 alpha 或按钮根本没做渐隐：
    ///     · 读得到 QTEButton.Duration 时，降到初始值的 40% 就点
    ///     · 无论如何，出生后超过「兜底延迟」秒就点（默认 1.0 秒），绝不漏
    ///
    /// === 本版的辅助策略（玩家反馈后重做）===
    ///   玩家反馈：「自动/默认开启的快速反应辅助不合理且破坏沉浸感」。
    ///   因此默认行为改为**只播报、不代点**，代点必须由玩家显式开启：
    ///
    ///   · 自动通过默认关闭（配置 [QTE] 自动通过 = false）。
    ///     关闭时 QTE 出现只念「反应环节。按 F3 可以自动通过。」；
    ///     之后每 20 秒最多提醒 3 次「反应环节还没通过。按 F3 可以自动通过。」
    ///   · F3 **任何时候都可用**，不再被配置门控：
    ///       QTE 在场   → 开/关自动通过，念「自动通过已开启。」/「自动通过已关闭。」
    ///       QTE 不在场 → 念「当前没有反应环节。反应环节出现时，按 F3 可以自动通过。」
    ///   · QTE 结束念「反应环节结束。」+ 面板上抓到的修正值（抓到才念）；
    ///     4 秒内再次出现则念「反应环节重新开始。」（重来）。
    ///   · 所有环境播报 interrupt=false（不打断剧情朗读）；只有 F3 的显式操作打断。
    ///   · 播报不写重读缓冲（0.2.1 用户要求：缓冲只放剧情正文与选项）。
    ///
    /// === 安全性 ===
    ///   错误选项没有扣分，所以「点一遍」在结果上严格优于「什么都不点」。
    ///   采集范围严格限制在 QTEUI 子树内，不会误碰别处的按钮。
    /// </summary>
    internal static class Qte
    {
        // ---- 播报文案集中在一处，方便和安装说明对照 ----
        private const string MsgStartAuto = "反应环节，自动通过中。";
        private const string MsgStartManual = "反应环节。按 F3 可以自动通过。";
        private const string MsgReopen = "反应环节重新开始。";
        private const string MsgHint = "按 F3 可以自动通过。";
        private const string MsgStillRunning = "反应环节还没通过。按 F3 可以自动通过。";
        private const string MsgNoQte = "当前没有反应环节。反应环节出现时，按 F3 可以自动通过。";
        private const string MsgAutoEnabled = "自动通过已开启。";
        private const string MsgAutoDisabled = "自动通过已关闭。";
        private const string MsgEnd = "反应环节结束。";

        private const float HintFirstDelay = 20f;   // 进场后多久开始提醒
        private const float HintInterval = 20f;     // 提醒间隔
        private const int HintMax = 3;              // 每局最多提醒次数
        private const float ReopenWindow = 4f;      // 结束多久内又出现算「重开」

        private static Naninovel.UI.QTEUI _ui;
        private static int _rescanAt;
        private static bool _live;
        private static bool _hintGiven;
        private static int _clicks;
        private static float _lastDump;
        private static float _startedAt;
        private static float _endedAt = -1000f;
        private static float _nextHintAt;
        private static int _hintCount;
        private static readonly List<string> _lastTexts = new List<string>();
        private static bool _typeBroken;

        /// <summary>运行时开关，初值取自配置，F3 可切；切换不写回配置。</summary>
        internal static bool AutoOn;

        private sealed class Btn
        {
            public float Born;
            public float PrevAlpha = -1f;
            public float PeakAlpha;
            public float FirstDuration = -1f;
            public bool Clicked;
            public float ClickedAlpha;
            public float ClickedAt;
        }

        private static readonly Dictionary<int, Btn> _state = new Dictionary<int, Btn>();
        private static readonly HashSet<int> _activeLast = new HashSet<int>();

        // ================= 面板定位 =================

        private static Naninovel.UI.QTEUI Find()
        {
            try
            {
                if (_ui != null && _ui) return _ui;
                if (Time.frameCount < _rescanAt) return null;
                _rescanAt = Time.frameCount + 20;      // 每 ~1/3 秒找一次
                _ui = UnityEngine.Object.FindObjectOfType<Naninovel.UI.QTEUI>();
                return _ui;
            }
            catch (Exception e)
            {
                if (!_typeBroken)
                {
                    _typeBroken = true;
                    Plugin.Diag("QTEUI 类型不可用，自动点击已停用: " + e.GetType().Name);
                    Plugin.L.LogWarning("[a11y] QTEUI 类型不可用，QTE 自动点击停用：" + e.GetType().Name);
                }
                return null;
            }
        }

        private static bool Live(Naninovel.UI.QTEUI ui)
        {
            try
            {
                if (ui == null || !ui) return false;
                if (!ui.gameObject.activeInHierarchy) return false;
                return !UiVis.Hidden(ui.transform);
            }
            catch { return false; }
        }

        // ================= 热键 =================

        private static KeyCode Hotkey()
        {
            try
            {
                string s = Plugin.CfgQteHotkey != null ? Plugin.CfgQteHotkey.Value : "F3";
                if (string.IsNullOrEmpty(s)) return KeyCode.F3;
                return (KeyCode)Enum.Parse(typeof(KeyCode), s.Trim(), true);
            }
            catch { return KeyCode.F3; }
        }

        /// <summary>
        /// F3：QTE 在场时切换自动通过；不在场时只说明用法。
        /// 返回这一帧是否处理了按键（Update 用它避免「刚切换完又叠加提示」）。
        /// 不受配置「自动通过」门控 —— 玩家任何一局都能用。
        /// </summary>
        private static bool HandleHotkey(bool live)
        {
            KeyCode k;
            try { k = Hotkey(); } catch { return false; }
            bool down;
            try { down = Input.GetKeyDown(k); } catch { return false; }
            if (!down) return false;

            if (!live)
            {
                Plugin.Diag("QTE 热键 " + k + "：当前没有反应环节");
                Speech.Speak(MsgNoQte, true);
                return true;
            }

            AutoOn = !AutoOn;
            Plugin.Diag("QTE 自动通过 -> " + (AutoOn ? "开" : "关") + "（" + k + "）");
            Speech.Speak(AutoOn ? MsgAutoEnabled : MsgAutoDisabled, true);
            // 切回手动后，把下一次提醒往后推，避免刚念完状态马上又念提醒
            if (!AutoOn) _nextHintAt = Mathf.Max(_nextHintAt, Time.realtimeSinceStartup + HintInterval);
            return true;
        }

        private static float FallbackDelay()
        {
            try
            {
                if (Plugin.CfgQteFallbackDelay != null) return Mathf.Max(0.15f, Plugin.CfgQteFallbackDelay.Value);
            }
            catch { }
            return 1.0f;
        }

        // ================= 主循环 =================

        internal static void Update()
        {
            // 1) 无条件定位/判定 QTE 面板（保持每 20 帧重扫）
            var ui = Find();
            bool live = Live(ui);

            // 2) F3 永远可用（不再被「自动通过」配置门控）
            bool hotkey = HandleHotkey(live);

            if (_typeBroken) return;

            // 3) live 边沿：结束/开始播报
            if (!live)
            {
                if (_live) EndSession();
                _hintGiven = false;
                return;
            }

            if (!_live) BeginSession();
            else if (!AutoOn && !hotkey) MaybeHint();

            // 4) 自动关闭时只提醒 + 抓文本，绝不代点；自动打开时才 Tick
            if (!AutoOn)
            {
                DumpScore(ui);
                return;
            }

            Tick(ui);
            DumpScore(ui);
        }

        /// <summary>QTE 开始：清状态、播报一次（重开时换文案）。</summary>
        private static void BeginSession()
        {
            _live = true;
            _clicks = 0;
            _startedAt = Time.realtimeSinceStartup;
            _hintCount = 0;
            _nextHintAt = _startedAt + HintFirstDelay;
            _state.Clear();
            _activeLast.Clear();
            _lastTexts.Clear();
            _lastDump = -1000f;      // 下一帧立刻抓一次面板文字

            bool reopen = _startedAt - _endedAt <= ReopenWindow;
            string msg;
            if (reopen) msg = AutoOn ? MsgReopen : MsgReopen + MsgHint;
            else msg = AutoOn ? MsgStartAuto : MsgStartManual;

            Plugin.Diag("QTE 出现，自动通过=" + (AutoOn ? "开" : "关")
                + " 兜底延迟=" + FallbackDelay().ToString("0.00") + "s"
                + (reopen ? "（重开）" : ""));

            if (!_hintGiven)
            {
                _hintGiven = true;
                Speech.Speak(msg, false);       // 不打断剧情朗读；不写重读缓冲
            }
        }

        /// <summary>QTE 结束：保留原日志，播报修正值（不打断）。</summary>
        private static void EndSession()
        {
            float now = Time.realtimeSinceStartup;
            Plugin.Diag("QTE 结束，共点了 " + _clicks + " 次（历时 "
                + Mathf.RoundToInt(now - _startedAt) + " 秒）");
            _endedAt = now;

            string score = PickScoreText();
            string msg = string.IsNullOrEmpty(score) ? MsgEnd : MsgEnd + score;
            if (!string.IsNullOrEmpty(score)) Plugin.Diag("QTE 结果播报: " + score);
            Speech.Speak(msg, false);           // 不打断剧情朗读；不写重读缓冲

            _live = false;
            _clicks = 0;
            _state.Clear();
            _activeLast.Clear();
        }

        /// <summary>自动关闭时每隔一段时间提醒一次（最多 3 次）。</summary>
        private static void MaybeHint()
        {
            if (_hintCount >= HintMax) return;
            float now = Time.realtimeSinceStartup;
            if (now - _startedAt < HintFirstDelay) return;
            if (now < _nextHintAt) return;

            _hintCount++;
            _nextHintAt = now + HintInterval;
            Plugin.Diag("QTE 提示 #" + _hintCount + "：仍在反应环节");
            Speech.Speak(MsgStillRunning, false);   // 不进重读缓冲
        }

        /// <summary>
        /// 从最近一次 DumpScore 抓到的面板文字里挑修正值：
        /// 含「修正值」或「%」的条目里取最长的一条，去掉空白。
        /// 若游戏把「修正值：」标签和「97%」数值分成两个 TMP 节点，
        /// 优先带数字的数值项（否则只会念出没有数字的标签）。
        /// </summary>
        private static string PickScoreText()
        {
            try
            {
                string bestValued = null;
                string bestAny = null;
                for (int i = 0; i < _lastTexts.Count; i++)
                {
                    string s = StripWhitespace(_lastTexts[i]);
                    if (string.IsNullOrEmpty(s)) continue;
                    bool hasPct = s.IndexOf('%') >= 0;
                    bool hasFix = s.IndexOf("修正值", StringComparison.Ordinal) >= 0;
                    if (!hasPct && !hasFix) continue;

                    if (bestAny == null || s.Length > bestAny.Length) bestAny = s;
                    if (HasDigit(s) && (bestValued == null || s.Length > bestValued.Length)) bestValued = s;
                }
                return bestValued != null ? bestValued : bestAny;
            }
            catch { return null; }
        }

        private static bool HasDigit(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
                if (s[i] >= '0' && s[i] <= '9') return true;
            return false;
        }

        private static string StripWhitespace(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
                if (!char.IsWhiteSpace(s[i])) sb.Append(s[i]);
            return sb.ToString();
        }

        private static void Tick(Naninovel.UI.QTEUI ui)
        {
            float now = Time.realtimeSinceStartup;
            var buttons = Collect(ui);
            var activeNow = new HashSet<int>();

            foreach (var b in buttons)
            {
                if (b == null || b.gameObject == null) continue;
                if (!b.gameObject.activeInHierarchy) continue;
                if (!b.interactable) continue;

                int id = b.GetInstanceID();
                activeNow.Add(id);

                Btn st;
                if (!_state.TryGetValue(id, out st) || !_activeLast.Contains(id))
                {
                    // 新的一次生命（按钮是池化的，实例 ID 会复用，所以按「不活跃 -> 活跃」判定）
                    st = new Btn { Born = now };
                    _state[id] = st;
                }

                if (st.Clicked) { st.PrevAlpha = AlphaOf(b); continue; }

                float a = AlphaOf(b);
                if (a > st.PeakAlpha) st.PeakAlpha = a;

                float dur = DurationOf(b, st);
                float age = now - st.Born;

                bool peaked = st.PrevAlpha >= 0f && a < st.PrevAlpha - 0.002f && st.PeakAlpha >= 0.35f;
                bool durDue = st.FirstDuration > 0f && dur > 0f && dur <= st.FirstDuration * 0.40f;
                bool tooOld = age >= FallbackDelay();

                if (peaked || durDue || tooOld)
                {
                    try
                    {
                        b.onClick.Invoke();
                        st.Clicked = true;
                        st.ClickedAlpha = a;
                        st.ClickedAt = now;
                        _clicks++;
                        if (_clicks <= 10)
                            Plugin.Diag(string.Format(
                                "QTE 点击 #{0}: 用时 {1:0.00}s alpha {2:0.00}（峰值 {3:0.00}）{4}",
                                _clicks, age, a, st.PeakAlpha,
                                peaked ? "峰值回落" : (durDue ? "时长将尽" : "兜底超时")));
                    }
                    catch (Exception e) { Plugin.Diag("QTE 点击失败: " + e.Message); }
                }

                st.PrevAlpha = a;
            }

            _activeLast.Clear();
            foreach (var id in activeNow) _activeLast.Add(id);
        }

        /// <summary>按钮当前的不透明度。优先它自己的 CanvasGroup，其次自身/子物体的 Image。</summary>
        private static float AlphaOf(Button b)
        {
            try
            {
                var cg = b.GetComponent<CanvasGroup>();
                if (cg != null) return cg.alpha;
            }
            catch { }
            try
            {
                var img = b.GetComponent<Image>();
                if (img != null) return img.color.a;
            }
            catch { }
            try
            {
                var img = b.GetComponentInChildren<Image>(true);
                if (img != null) return img.color.a;
            }
            catch { }
            return 1f;
        }

        /// <summary>读 QTEButton 的剩余时长（拿不到就返回 -1）。</summary>
        private static float DurationOf(Button b, Btn st)
        {
            try
            {
                var bl = _ui != null ? _ui.buttonList : null;
                if (bl == null) return -1f;
                for (int i = 0; i < bl.Count; i++)
                {
                    var qb = bl[i];
                    if (qb == null) continue;
                    if (qb.Button != b) continue;
                    float d = qb.Duration;
                    if (st.FirstDuration < 0f) st.FirstDuration = d;
                    return d;
                }
            }
            catch { }
            return -1f;
        }

        /// <summary>
        /// 每 2 秒把面板上的文字抓一次（写日志，同时缓存最近一次抓取，
        /// 供结束时播报修正值用）。
        /// </summary>
        private static void DumpScore(Naninovel.UI.QTEUI ui)
        {
            if (Time.realtimeSinceStartup - _lastDump < 2f) return;
            _lastDump = Time.realtimeSinceStartup;
            try
            {
                var tmps = ui.GetComponentsInChildren<TMPro.TMP_Text>(false);
                var sb = new System.Text.StringBuilder("QTE 面板文字: ");
                _lastTexts.Clear();
                int c = 0;
                if (tmps != null)
                {
                    foreach (var t in tmps)
                    {
                        if (t == null || string.IsNullOrEmpty(t.text)) continue;
                        string txt = t.text.Trim();
                        _lastTexts.Add(txt);
                        sb.Append('[').Append(txt).Append("] ");
                        if (++c >= 12) break;
                    }
                }
                if (c == 0) sb.Append("(无)");
                Plugin.Diag(sb.ToString());
            }
            catch { }
        }

        private static List<Button> Collect(Naninovel.UI.QTEUI ui)
        {
            var list = new List<Button>();
            try
            {
                var bl = ui.buttonList;
                if (bl != null)
                {
                    for (int i = 0; i < bl.Count; i++)
                    {
                        var qb = bl[i];
                        if (qb == null) continue;
                        var b = qb.Button;
                        if (b != null) list.Add(b);
                    }
                }
            }
            catch { }

            if (list.Count == 0)
            {
                try
                {
                    var all = ui.GetComponentsInChildren<Button>(false);
                    if (all != null) foreach (var b in all) if (b != null) list.Add(b);
                }
                catch { }
            }
            return list;
        }
    }
}
