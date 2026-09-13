using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace NoExistenceA11y
{
    /// <summary>
    /// 选项框朗读与键盘选择。
    ///
    /// 为什么这块是必须的：本作主角**从不用文本说话**，全部台词都在选项框里
    /// （全剧 ≥625 条、8000 多字）。对明眼玩家这是「点一下」，对读屏玩家
    /// 这 8000 字就是主角唯一的语音轨 —— 而原本只能用鼠标。
    ///
    /// === 三个实测出来的坑（v0.1.0 → v0.2.0）===
    ///
    /// 1) 【不能在 AddChoiceButton 里读文字】
    ///    那个回调触发时按钮刚 Create，TMP 上的文字还没写上去，读到的是空串。
    ///    现在改成每帧扫一遍容器，内容变了才播报。
    ///
    /// 2) 【容器第 0 个子物体不是第一个选项】
    ///    直接按 childIndex 编号会整体偏一位（按 2 才选到第 1 项）。
    ///    现在先过滤出「激活 + 有 Button + 有文字」的子物体，再按这个列表编号。
    ///
    /// 3) 【文字可能在子物体里，而且不止一段】
    ///    标签要拼接子树里所有非空文本，只取第一个会读到空。
    /// </summary>
    internal static class Choices
    {
        private static Naninovel.UI.ChoiceHandlerPanel _panel;
        private static readonly List<string> _labels = new List<string>();
        private static readonly List<Button> _buttons = new List<Button>();
        private static string _announcedKey = "";
        private static bool _announcedHeader;
        private static int _lastChildCount = -1;

        /// <summary>由 Harmony 在 AddChoiceButton 之后调用：只记下面板，不做朗读。</summary>
        internal static void OnButtonAdded(Naninovel.UI.ChoiceHandlerPanel panel)
        {
            if (panel != null) _panel = panel;
        }

        private static Naninovel.UI.ChoiceHandlerPanel Panel()
        {
            try
            {
                if (_panel != null && _panel) return _panel;
                _panel = UnityEngine.Object.FindObjectOfType<Naninovel.UI.ChoiceHandlerPanel>();
                return _panel;
            }
            catch { return null; }
        }

        private static Transform Container(Naninovel.UI.ChoiceHandlerPanel panel)
        {
            try
            {
                var c = panel.ButtonsContainer;
                return c == null ? null : c.transform;
            }
            catch { return null; }
        }

        /// <summary>子树里所有非空文本拼起来（最多 3 段）。</summary>
        private static string TextOf(Transform t)
        {
            try
            {
                var parts = new List<string>();
                var tmps = t.GetComponentsInChildren<TMPro.TMP_Text>(true);
                if (tmps != null)
                {
                    foreach (var x in tmps)
                    {
                        if (x == null) continue;
                        string s = x.text;
                        if (string.IsNullOrEmpty(s)) continue;
                        s = s.Trim();
                        if (s.Length == 0 || parts.Contains(s)) continue;
                        parts.Add(s);
                        if (parts.Count >= 3) break;
                    }
                }
                if (parts.Count == 0)
                {
                    var txts = t.GetComponentsInChildren<UnityEngine.UI.Text>(true);
                    if (txts != null)
                    {
                        foreach (var x in txts)
                        {
                            if (x == null) continue;
                            string s = x.text;
                            if (string.IsNullOrEmpty(s)) continue;
                            s = s.Trim();
                            if (s.Length == 0 || parts.Contains(s)) continue;
                            parts.Add(s);
                            if (parts.Count >= 3) break;
                        }
                    }
                }
                return string.Join(" ", parts.ToArray());
            }
            catch { return ""; }
        }

        /// <summary>选项里的「（动作）」是舞台指示，念的时候去掉括号更像人话。</summary>
        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = TextProc.ToSpeech(s);
            s = s.Replace("（", "").Replace("）", "").Replace("(", "").Replace(")", "");
            return s.Trim();
        }

        private static void Refresh(Naninovel.UI.ChoiceHandlerPanel panel)
        {
            _labels.Clear();
            _buttons.Clear();

            var container = Container(panel);
            if (container == null) return;

            int n = container.childCount;
            _lastChildCount = n;
            var sb = Plugin.CfgDiag != null && Plugin.CfgDiag.Value ? new StringBuilder() : null;
            if (sb != null) sb.Append("选项容器 ").Append(container.name).Append(" childCount=").Append(n);

            for (int i = 0; i < n; i++)
            {
                Transform ch = null;
                try { ch = container.GetChild(i); } catch { }
                if (ch == null) continue;

                bool live;
                try { live = ch.gameObject.activeInHierarchy; } catch { live = false; }

                Button b = null;
                try { b = ch.GetComponent<Button>(); } catch { }
                if (b == null) { try { b = ch.GetComponentInChildren<Button>(true); } catch { } }

                string label = TextOf(ch);

                if (sb != null)
                    sb.Append("\n    [").Append(i).Append("] ").Append(live ? "激活" : "未激活")
                      .Append(b != null ? " 有Button" : " 无Button")
                      .Append(" 文字=").Append(string.IsNullOrEmpty(label) ? "(空)" : label);

                // 只把「激活 + 点得动 + 有文字」的当成真正的选项
                if (!live || b == null || string.IsNullOrEmpty(label)) continue;
                if (!b.interactable) continue;

                _labels.Add(Clean(label));
                _buttons.Add(b);
            }

            if (sb != null && n > 0)
            {
                sb.Append("\n    => 认定为选项 ").Append(_labels.Count).Append(" 项");
                Plugin.Diag(sb.ToString());
            }
        }

        internal static void Update()
        {
            var panel = Panel();

            // 面板「在画面上」= 物体激活 **且** 没有被 CanvasGroup 藏起来。
            // 只看 activeInHierarchy 会踩坑：选完之后那批按钮是延迟移除的，
            // 如果这时面板被淡出，残留的按钮会被当成新选项念出来 ——
            // 表现就是「念了一些不属于当前剧情的选项」。
            bool live = panel != null && UiVis.Visible(panel);

            if (!live)
            {
                if (_labels.Count > 0) { _labels.Clear(); _buttons.Clear(); _announcedKey = ""; _announcedHeader = false; }
                return;
            }

            bool wantRead = Plugin.CfgReadChoices == null || Plugin.CfgReadChoices.Value;

            // 容器子物体数量变了就重扫（每帧全量扫代价太高）
            int cc = -1;
            try { var c = Container(panel); if (c != null) cc = c.childCount; } catch { }
            if (cc != _lastChildCount || _labels.Count == 0) Refresh(panel);

            // 文字是按钮创建之后才写上去的，所以「内容变了才播报」这个判据
            // 天然把「读到空串」那一版挡掉了。
            string key = string.Join("|", _labels.ToArray());
            if (key.Length > 0 && key != _announcedKey && _labels.Count > 0)
            {
                _announcedKey = key;
                if (wantRead)
                {
                    if (!_announcedHeader) { Speech.Speak("选项。", false); _announcedHeader = true; }
                    for (int i = 0; i < _labels.Count; i++)
                        Speech.Speak((i + 1) + "。" + _labels[i], false);
                }
                Plugin.Diag("CHOICE 播报 " + _labels.Count + " 项: " + key);
            }

            // ---- 数字键直选 ----
            if (Plugin.CfgChoiceHotkeys == null || !Plugin.CfgChoiceHotkeys.Value) return;
            if (_buttons.Count == 0) return;

            int pick = 0;
            try
            {
                for (int i = 1; i <= 9; i++)
                {
                    if (Input.GetKeyDown(KeyCode.Alpha0 + i) || Input.GetKeyDown(KeyCode.Keypad0 + i))
                    {
                        pick = i;
                        break;
                    }
                }
            }
            catch { return; }
            if (pick == 0) return;

            if (pick > _buttons.Count)
            {
                Speech.Speak("只有 " + _buttons.Count + " 个选项。", true);
                return;
            }

            try
            {
                var b = _buttons[pick - 1];
                if (b == null || b.gameObject == null) { Speech.Speak("选项已失效。", true); return; }

                if (wantRead) Speech.Speak(_labels[pick - 1], true);   // 复述一遍，确认选的是哪个

                b.onClick.Invoke();
                Plugin.Diag("CHOICE 数字键 " + pick + " -> " + _labels[pick - 1]);
            }
            catch (Exception e)
            {
                Plugin.Diag("Choices.Update: " + e.Message);
                Speech.Speak("选择失败。", true);
            }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Naninovel.UI.ChoiceHandlerPanel), "AddChoiceButton")]
    public static class ChoicePanelPatch
    {
        [HarmonyLib.HarmonyPostfix]
        public static void Postfix(Naninovel.UI.ChoiceHandlerPanel __instance)
            => Choices.OnButtonAdded(__instance);
    }
}
