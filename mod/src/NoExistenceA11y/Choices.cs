using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
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

        // 离开选项时如果玩家还按着回车，先不还原 uGUI Submit（否则还原那一帧
        // 原生 Submit 会点掉别的按钮）；等键松开再还原。
        private static bool _navRestorePending;
        private static bool _navRestoreLogged;

        // 选项面板在场时还要关掉 uGUI 的提交通路（2026-11-22 实机反馈的根因）：
        // ChoiceHandlerPanel 会给选项按钮设 EventSystem 选中态（FocusAddedButton），
        // 于是玩家按回车时 uGUI 的 Submit 会**先**把「当前选中」的那个选项点掉，
        // 我们补丁随后又点播报的那个 —— 两个选项都触发，游戏执行了原生的那个。
        // 关掉 sendNavigationEvents 只影响输入模块的 Submit/Move 派发，
        // 不影响我们用 onClick.Invoke() 主动激活，是代价最小的一刀。
        private static bool _uguiNavSaved;
        private static bool _uguiNavWasOn;

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

        /// <summary>诊断用：从容器往上拼几层路径，便于对照游戏侧控件。</summary>
        private static string PathOf(Transform t)
        {
            try
            {
                var parts = new List<string>();
                Transform cur = t;
                int guard = 0;
                while (cur != null && guard < 6)
                {
                    parts.Insert(0, cur.name);
                    cur = cur.parent;
                    guard++;
                }
                return string.Join("/", parts.ToArray());
            }
            catch { return ""; }
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

                // 2026-11-22 实机：选项与实际选择对不上。怀疑容器子物体顺序 ≠ 游戏
                // 逻辑顺序（自定义按钮/绝对定位/池化）。把「按钮自己记的 ChoiceState」
                // 也打出来：id 与坐标是游戏侧的权威数据，复测时一眼能看出错位。
                if (sb != null)
                {
                    try { sb.Append(" sib=").Append(ch.GetSiblingIndex()); } catch { }
                    try
                    {
                        if (b != null)
                        {
                            var chb = b.GetComponent<Naninovel.UI.ChoiceHandlerButton>();
                            if (chb != null)
                            {
                                var cs = chb.ChoiceState;
                                sb.Append(" id=").Append(cs.Id);
                                sb.Append(" pos=").Append(cs.ButtonPosition.x.ToString("0")).Append(",").Append(cs.ButtonPosition.y.ToString("0"));
                            }
                        }
                    }
                    catch { }
                    sb.Append(" path=").Append(PathOf(ch));
                }

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

            // 兜底（2026-11-22）：多套选项控件的实测反馈里有「部分选项数字键选不动」。
            // 如果容器子物体里一个 Button 都没认出来（变体面板把按钮挂在别的层级、
            // 或用了自定义按钮 prefab），直接按 ChoiceHandlerButton 组件抓一遍。
            if (_buttons.Count == 0)
            {
                try
                {
                    var found = panel.GetComponentsInChildren<Naninovel.UI.ChoiceHandlerButton>(true);
                    if (found != null && found.Length > 0)
                    {
                        for (int i = 0; i < found.Length; i++)
                        {
                            var chb = found[i];
                            if (chb == null) continue;
                            bool live2;
                            try { live2 = chb.gameObject.activeInHierarchy; } catch { live2 = false; }
                            if (!live2) continue;
                            var b2 = chb.GetComponent<Button>();
                            if (b2 == null) continue;
                            if (!b2.interactable) continue;
                            string label2 = TextOf(chb.transform);
                            if (string.IsNullOrEmpty(label2)) continue;
                            _labels.Add(Clean(label2));
                            _buttons.Add(b2);
                        }
                        if (_labels.Count > 0)
                            Plugin.Diag("CHOICE 兜底扫描：容器没认出按钮，按 ChoiceHandlerButton 找到 " + _labels.Count + " 项");
                    }
                }
                catch (Exception e) { Plugin.Diag("Choices.Refresh 兜底: " + e.Message); }
            }
        }

        internal static void Update()
        {
            // 离开选项时若玩家还按着 Submit，等松开再还原 uGUI 导航（否则还原帧会被原生 Submit 点掉）。
            if (_navRestorePending)
            {
                bool held0 = false;
                try { held0 = Input.GetButton("Submit"); } catch { }
                if (!held0)
                {
                    _navRestorePending = false;
                    _navRestoreLogged = false;
                    SuppressUguiSubmit(false);
                }
            }

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
                SuppressUguiSubmit(false);
                ReleaseInputBlock();
                return;
            }

            // 选项面板一出现就关掉 uGUI Submit（见字段注释）。放在 BlockInput 之前：
            // 哪怕 Naninovel 的 AddBlockingUI 因面板还没登记而失败，这一刀也生效。
            SuppressUguiSubmit(true);
            if (!_inputBlocked) BlockInput();

            bool wantRead = Plugin.CfgReadChoices == null || Plugin.CfgReadChoices.Value;

            // 容器子物体数量变了就重扫（每帧全量扫代价太高）
            int cc = -1;
            try { var c = Container(panel); if (c != null) cc = c.childCount; } catch { }
            // 2026-11-22 EXP2：除了数量变化，按钮被池化替换/隐藏时也要重扫。
            // 变体选项面板可能在同一个 childCount 里把旧按钮换掉（消失式选项），
            // 只看数量会沿用旧按钮对象，出现「念的是新选项、点的还是旧按钮」。
            bool stale = false;
            for (int i = 0; i < _buttons.Count; i++)
            {
                var bt = _buttons[i];
                if (bt == null || bt.gameObject == null || !bt.gameObject.activeInHierarchy) { stale = true; break; }
            }
            if (cc != _lastChildCount || _labels.Count == 0 || stale) Refresh(panel);

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
                if (b == null || b.gameObject == null || !b.gameObject.activeInHierarchy)
                {
                    // 池化/消失式选项：列表可能已经换了一轮，重扫后再按同一个序号取。
                    var pnl = Panel();
                    if (pnl != null) Refresh(pnl);
                    if (index < 0 || index >= _buttons.Count)
                    {
                        Speech.Speak("选项已变化，请重新选择。", true);
                        return;
                    }
                    b = _buttons[index];
                    if (b == null || b.gameObject == null || !b.gameObject.activeInHierarchy)
                    {
                        Speech.Speak("选项已变化，请重新选择。", true);
                        return;
                    }
                }

                if (speakLabel) Speech.Speak(_labels[index], true);   // 复述一遍，确认选的是哪个

                _choiceIndex = index;
                // Invoke 与真实点击同链，但面板业务 lambda 受 IInputManager.ProcessInput 门禁：
                // 若它此刻是 false，Invoke 会被静默丢弃（听起来像「按了没反应」）。
                try
                {
                    var im = Naninovel.Engine.GetService<Naninovel.IInputManager>();
                    if (im != null && !im.ProcessInput)
                    {
                        im.ProcessInput = true;
                        Plugin.Diag("CHOICE 检测到 ProcessInput=false，已恢复 true（否则选择会被静默丢弃）");
                    }
                }
                catch { }
                try
                {
                    EventSystem es = EventSystem.current;
                    string sel = (es != null && es.currentSelectedGameObject != null)
                        ? PathOf(es.currentSelectedGameObject.transform) : "(无)";
                    Plugin.Diag("CHOICE 激活前：selected=" + sel
                        + "，sendNavigationEvents=" + (es != null ? es.sendNavigationEvents.ToString() : "?"));
                }
                catch { }
                b.onClick.Invoke();
                Plugin.Diag("CHOICE 激活 " + (index + 1) + " -> " + _labels[index]
                    + " path=" + PathOf(b.transform) + "，按钮总数=" + _buttons.Count);
            }
            catch (Exception e)
            {
                Plugin.Diag("Choices.Activate: " + e.Message);
                Speech.Speak("选择失败。", true);
            }
        }

        /// <summary>选项面板在场时关掉 uGUI 的 Submit/Move 派发；离开时还原。</summary>
        private static void SuppressUguiSubmit(bool on)
        {
            try
            {
                if (on)
                {
                    if (_uguiNavSaved) return;
                    EventSystem es = EventSystem.current;
                    if (es == null) return;
                    _uguiNavWasOn = es.sendNavigationEvents;
                    _uguiNavSaved = true;
                    if (es.sendNavigationEvents) es.sendNavigationEvents = false;
                    // 面板出现时游戏会 FocusAddedButton（帧末 SetSelectedGameObject）。
                    // 清掉残留选中，避免还原/首帧时原生 Submit 点到旧按钮。
                    try { if (es.currentSelectedGameObject != null) es.SetSelectedGameObject(null); } catch { }
                    Plugin.Diag("CHOICE 已关闭 uGUI Submit（sendNavigationEvents 原值=" + _uguiNavWasOn + "）并清掉选中态，避免原生选中抢先确认");
                }
                else
                {
                    if (!_uguiNavSaved) return;
                    // 玩家还按着回车/空格时不还原：还原那一帧原生 Submit 会点掉当前选中。
                    bool held = false;
                    try { held = Input.GetButton("Submit"); } catch { }
                    if (held)
                    {
                        _navRestorePending = true;
                        if (!_navRestoreLogged)
                        {
                            _navRestoreLogged = true;
                            Plugin.Diag("CHOICE 等待 Submit 松开后再还原 uGUI Submit");
                        }
                        return;
                    }
                    _navRestorePending = false;
                    _navRestoreLogged = false;
                    _uguiNavSaved = false;
                    EventSystem es = EventSystem.current;
                    if (es != null) es.sendNavigationEvents = _uguiNavWasOn;
                    Plugin.Diag("CHOICE 已还原 uGUI Submit=" + _uguiNavWasOn);
                }
            }
            catch (Exception e) { Plugin.Diag("Choices.SuppressUguiSubmit: " + e.Message); }
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
                if (ui == null)
                {
                    Plugin.Diag("CHOICE BlockInput：面板根找不到 IManagedUI（按钮数=" + _buttons.Count
                        + " path=" + PathOf(panel.transform) + "），本轮 Continue/Submit 未挡住");
                    return;
                }
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
