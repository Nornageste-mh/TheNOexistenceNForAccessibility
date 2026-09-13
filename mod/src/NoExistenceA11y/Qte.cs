using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NoExistenceA11y
{
    /// <summary>
    /// QTE（烤箱那段）自动点击。
    ///
    /// === 实机确认的形态（由玩家描述）===
    ///   屏幕上散落着大小不一的选项按钮，**一段时间内不点就消失**。
    ///   其中「正确选项」增加分数「修正值」，「错误选项」点了无事发生。
    ///   得分低于阈值要重来。这是**纯反应式**关卡，画面上没有任何可听的线索 ——
    ///   对盲人玩家不是难，是没有通道。
    ///
    ///   玩家实测还能硬闯：进导航模式，方向键 + 回车反复盲按。
    ///   因为点错没有惩罚，所以「全点一遍」确实能过 —— 但那是在殴打它，不是玩它。
    ///
    /// === 重要性质：它不可能让局面变坏 ===
    ///   错误选项「点了无事发生」，没有扣分。所以「把出现的按钮都点一遍」
    ///   在结果上**严格优于**「什么都不点」：点对了加分，点错了不扣。
    ///   因此哪怕这个功能完全没生效，玩家的处境也和没有它时一模一样。
    ///   采集范围严格限制在 QTEUI 子树内，不会误碰别处的按钮。
    ///
    /// === 版本历史 ===
    ///   v0.0.0.1：靠 `GameObject.Find("QTEPanel")` 猜对象名 —— 三轮实机日志里
    ///             一次都没识别到（全是 null）。从未生效过。
    ///   v0.0.0.2：改为直接引用游戏程序集里的 `Naninovel.UI.QTEUI`，用
    ///             FindObjectOfType 定位；并加上 F3 手动开关便于预览。
    ///
    /// === 操作 ===
    ///   F3（可在配置里改）—— 开 / 关自动点击，切换时会念出来。
    ///   面板出现而自动点击关着时，会提示一次「按 F3 可以自动通过」。
    /// </summary>
    internal static class Qte
    {
        private static Naninovel.UI.QTEUI _ui;
        private static int _rescanAt;
        private static bool _live;
        private static bool _hintGiven;
        private static int _clicks;
        private static float _lastDump;
        private static float _startedAt;
        private static bool _typeBroken;

        /// <summary>运行时开关，初值取自配置，F3 可切。</summary>
        internal static bool AutoOn;

        /// <summary>上一帧处于激活状态的按钮，用来找「激活沿」。</summary>
        private static readonly HashSet<int> _wasActive = new HashSet<int>();

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
                // 游戏改版把类型删了会走到这里：记一次，然后彻底停用，不要再刷屏
                if (!_typeBroken)
                {
                    _typeBroken = true;
                    Plugin.Diag("QTEUI 类型不可用，自动点击已停用: " + e.GetType().Name);
                    Plugin.L.LogWarning("[a11y] QTEUI 类型不可用，QTE 自动点击停用：" + e.GetType().Name);
                }
                return null;
            }
        }

        /// <summary>面板此刻是否真的在玩家眼前。CustomUI 靠 CanvasGroup 显隐，必须查。</summary>
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

        private static void HandleHotkey()
        {
            KeyCode k;
            try { k = Hotkey(); } catch { return; }
            bool down;
            try { down = Input.GetKeyDown(k); } catch { return; }
            if (!down) return;

            AutoOn = !AutoOn;
            Plugin.Diag("QTE 自动点击 -> " + (AutoOn ? "开" : "关") + "（" + k + "）");
            Speech.Speak(AutoOn ? "自动点击已开启。" : "自动点击已关闭。", true);
        }

        internal static void Update()
        {
            if (Plugin.CfgQteAutoPass == null || !Plugin.CfgQteAutoPass.Value) return;

            HandleHotkey();
            if (_typeBroken) return;

            var ui = Find();
            bool live = Live(ui);

            if (!live)
            {
                if (_live)
                {
                    Plugin.Diag("QTE 结束，共点了 " + _clicks + " 次（历时 "
                        + Mathf.RoundToInt(Time.realtimeSinceStartup - _startedAt) + " 秒）");
                    _live = false;
                    _clicks = 0;
                    _wasActive.Clear();
                }
                _hintGiven = false;
                return;
            }

            if (!_live)
            {
                _live = true;
                _clicks = 0;
                _startedAt = Time.realtimeSinceStartup;
                _wasActive.Clear();
                Plugin.Diag("QTE 出现，自动点击=" + (AutoOn ? "开" : "关")
                    + " 面板=" + PathOf(ui.transform));
                Speech.Speak(AutoOn ? "反应环节，已自动通过。" : "反应环节。按 F3 可以自动通过。", true);
            }
            else if (!AutoOn && !_hintGiven)
            {
                _hintGiven = true;
                Speech.Speak("按 F3 可以自动通过。", false);
            }

            if (!AutoOn)
            {
                DumpScore(ui);
                return;
            }

            // ---- 点掉这一帧新出现的按钮 ----
            int fired = 0;
            try
            {
                var buttons = Collect(ui);
                var nowActive = new HashSet<int>();
                foreach (var b in buttons)
                {
                    if (b == null || b.gameObject == null) continue;
                    if (!b.gameObject.activeInHierarchy) continue;
                    if (!b.interactable) continue;

                    int id = b.GetInstanceID();
                    nowActive.Add(id);
                    if (_wasActive.Contains(id)) continue;   // 上一帧就在，说明已经点过

                    b.onClick.Invoke();
                    fired++;
                }
                _wasActive.Clear();
                foreach (var id in nowActive) _wasActive.Add(id);
            }
            catch (Exception e) { Plugin.Diag("Qte.Update: " + e.Message); }

            if (fired > 0)
            {
                _clicks += fired;
                if (_clicks <= 8 || _clicks % 25 == 0)
                    Plugin.Diag("QTE 点了 " + fired + " 个新按钮（累计 " + _clicks + "）");
            }

            DumpScore(ui);
        }

        /// <summary>每 2 秒把面板上的文字抓一次，用来判定分数。</summary>
        private static void DumpScore(Naninovel.UI.QTEUI ui)
        {
            if (Time.realtimeSinceStartup - _lastDump < 2f) return;
            _lastDump = Time.realtimeSinceStartup;
            try
            {
                var tmps = ui.GetComponentsInChildren<TMPro.TMP_Text>(false);
                var sb = new System.Text.StringBuilder("QTE 面板文字: ");
                int c = 0;
                if (tmps != null)
                {
                    foreach (var t in tmps)
                    {
                        if (t == null || string.IsNullOrEmpty(t.text)) continue;
                        sb.Append('[').Append(t.text.Trim()).Append("] ");
                        if (++c >= 12) break;
                    }
                }
                if (c == 0) sb.Append("(无)");
                Plugin.Diag(sb.ToString());
            }
            catch { }
        }

        /// <summary>
        /// 收集面板上的按钮。优先走 QTEUI 自己的 buttonList（每个 QTEButton 带一个 Button），
        /// 拿不到就退回「面板子树里所有 Button」——始终限制在 QTEUI 子树内。
        /// </summary>
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

        private static string PathOf(Transform t)
        {
            try
            {
                var sb = new System.Text.StringBuilder(t.name);
                Transform cur = t.parent;
                int guard = 0;
                while (cur != null && guard++ < 12) { sb.Insert(0, cur.name + "/"); cur = cur.parent; }
                return sb.ToString();
            }
            catch { return "?"; }
        }
    }
}
