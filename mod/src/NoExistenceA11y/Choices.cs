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
        /// <summary>选项框此刻是不是真的在画面上（UiNav 用它决定"要不要屏蔽游戏输入"）。</summary>
        internal static bool Live;

        private static string _announcedKey = "";
        private static int _lastChildCount = -1;

        // 上下键浏览到的选项（0 基）。数字键仍可直接选，两条路都走 Activate()。
        private static int _choiceIndex;

        // 选项面板在场时，替它挡住 Continue/Submit：回车只用来确认选项，
        // 不会被游戏同时当成「推进一句」。做法与 UiNav 接管界面时一致。
        private static bool _inputBlocked;
        private static Naninovel.UI.IManagedUI _blockedUi;
        private static float _nextBlockTry;

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
            Live = live;

            if (!live)
            {
                if (_labels.Count > 0) { _labels.Clear(); _buttons.Clear(); _announcedKey = ""; }
                _choiceIndex = 0;
                ReleaseInputBlock();
                return;
            }

            if (!_inputBlocked) BlockInput();

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
                    // ★ 整组选项拼成**一段**念出去，与 TransparentHer 一致：
                    //     sb.Append("共 ").Append(n).Append(" 个选项。");
                    //     for (...) sb.Append("选项 ").Append(i+1).Append("：").Append(Label).Append("。");
                    //     sb.Append("按数字键选择。");  Say(sb.ToString());
                    //
                    // 两个理由：
                    //   1. 逐条念的时候，最后记住的只有「最后一条选项」——
                    //      玩家按退格想重听选项，结果只听到一项。
                    //      拼成一段再记住，退格重读的就是**整组选项**。
                    //   2. 一次念完也让「选项」在语义上是一件事，而不是几句话。
                    var sb = new System.Text.StringBuilder();
                    sb.Append("共 ").Append(_labels.Count).Append(" 个选项。");
                    for (int i = 0; i < _labels.Count; i++)
                        sb.Append("选项 ").Append(i + 1).Append("：").Append(_labels[i]).Append("。");
                    if (Plugin.CfgChoiceHotkeys == null || Plugin.CfgChoiceHotkeys.Value)
                        sb.Append("按数字键选择，或用上下键浏览、回车确认。");
                    else
                        sb.Append("用上下键浏览、回车确认。");

                    string announce = sb.ToString();
                    Plugin.Remember(announce);            // 退格能重读整组选项
                    Speech.Speak(announce, false);
                }
                Plugin.Diag("CHOICE 播报 " + _labels.Count + " 项: " + key);
            }

            if (_buttons.Count == 0) return;
            if (_choiceIndex < 0) _choiceIndex = 0;
            if (_choiceIndex >= _buttons.Count) _choiceIndex = _buttons.Count - 1;

            // ---- 数字键直选 ----
            if (Plugin.CfgChoiceHotkeys == null || Plugin.CfgChoiceHotkeys.Value)
            {
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

                if (pick != 0)
                {
                    if (pick > _buttons.Count) { Speech.Speak("只有 " + _buttons.Count + " 个选项。", true); return; }
                    Activate(pick - 1, wantRead);
                    return;
                }
            }

            // ---- 上下键浏览 + 回车确认（与数字键并列的另一条路）----
            try
            {
                if (Input.GetKeyDown(KeyCode.UpArrow))
                {
                    _choiceIndex = (_choiceIndex - 1 + _buttons.Count) % _buttons.Count;
                    SpeakChoice();
                    return;
                }
                if (Input.GetKeyDown(KeyCode.DownArrow))
                {
                    _choiceIndex = (_choiceIndex + 1) % _buttons.Count;
                    SpeakChoice();
                    return;
                }
                if (Input.GetKeyDown(KeyCode.Home)) { _choiceIndex = 0; SpeakChoice(); return; }
                if (Input.GetKeyDown(KeyCode.End)) { _choiceIndex = _buttons.Count - 1; SpeakChoice(); return; }

                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space))
                {
                    Activate(_choiceIndex, wantRead);
                    return;
                }
            }
            catch (Exception e)
            {
                Plugin.Diag("Choices.Update(键盘): " + e.Message);
            }
        }

        /// <summary>播报当前浏览到的选项（上下键移动时用，打断上一次）。</summary>
        private static void SpeakChoice()
        {
            try
            {
                if (_choiceIndex < 0 || _choiceIndex >= _labels.Count) return;
                Speech.Speak("选项 " + (_choiceIndex + 1) + "，共 " + _labels.Count + "：" + _labels[_choiceIndex], true);
            }
            catch { }
        }

        /// <summary>选中第 index 项（0 基）：数字键与回车共用这一个出口。</summary>
        private static void Activate(int index, bool speakLabel)
        {
            try
            {
                if (index < 0 || index >= _buttons.Count) { Speech.Speak("选项已失效。", true); return; }
                var b = _buttons[index];
                if (b == null || b.gameObject == null) { Speech.Speak("选项已失效。", true); return; }

                if (speakLabel) Speech.Speak(_labels[index], true);   // 复述一遍，确认选的是哪个

                _choiceIndex = index;
                b.onClick.Invoke();
                Plugin.Diag("CHOICE 激活 " + (index + 1) + " -> " + _labels[index]);
            }
            catch (Exception e)
            {
                Plugin.Diag("Choices.Activate: " + e.Message);
                Speech.Speak("选择失败。", true);
            }
        }

        /// <summary>选项面板在场时替它挡住 Continue/Submit：回车只用来确认选项，不会顺带推进剧情。</summary>
        private static void BlockInput()
        {
            if (Time.realtimeSinceStartup < _nextBlockTry) return;
            _nextBlockTry = Time.realtimeSinceStartup + 1f;
            try
            {
                var panel = Panel();
                if (panel == null) return;
                var im = Naninovel.Engine.GetService<Naninovel.IInputManager>();
                if (im == null) return;
                var ui = UiNav.FindManagedUi(panel.transform);
                if (ui == null) return;
                im.AddBlockingUI(ui, new string[] { "Continue", "Submit" });
                _blockedUi = ui;
                _inputBlocked = true;
            }
            catch (Exception e) { Plugin.Diag("Choices.BlockInput: " + e.Message); }
        }

        private static void ReleaseInputBlock()
        {
            if (!_inputBlocked) return;
            _inputBlocked = false;
            try
            {
                var im = Naninovel.Engine.GetService<Naninovel.IInputManager>();
                if (im != null && _blockedUi != null) im.RemoveBlockingUI(_blockedUi);
            }
            catch { }
            _blockedUi = null;
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
