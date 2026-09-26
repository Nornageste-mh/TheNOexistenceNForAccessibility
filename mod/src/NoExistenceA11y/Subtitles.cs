using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace NoExistenceA11y
{
    /// <summary>
    /// 「不经打印器」的两块文字面板的朗读：**字幕** 与 **笔记本（假回想）**。
    ///
    /// === 一、字幕（剧本里的 `@subtitle`）===
    ///
    /// 剧本里只有一句 `@subtitle FakeED`（Assembly-CSharp 的 CustomSubtitle 命令），
    /// 画面由 SubtitleUI 播一段 Animation 完成：
    ///
    ///   SubtitleUI
    ///     VocalConcert/01..13                    ← 唱歌那段
    ///     FakeED/node/1..31/{left,middle,right}  ← 「伪 ED」，整块往上滚
    ///     ED/right/01..10/…                      ← 真 ED，一屏一屏出现
    ///
    /// 文字是预制体里的 TMP 节点，由 ManagedTextProvider 填本地化文本，
    /// 既没有 RevealableText、也没有 OnPrintTextStarted —— 只挂打印器的话这段一个字都不念
    /// （玩家报告：「第四章结尾的字幕不可读」）。
    ///
    /// === 二、笔记本（`@FakeBackLog`）===
    ///
    /// 剧情里「打开笔记本」不是把文字打进对话框，而是 `@FakeBackLog` 命令
    /// 把 Story 的**假回想面板**（`BacklogFakeUI` / `BackLogFakePanel`）摊开，
    /// 里面每条是 `CustomMessage<N>` 节点（作者名 + 正文两个 TMP）。
    /// 剧本里的顺序是：`@PrintText` → `@HidePrinter` → `@FakeBackLog` → `@WaitForInput`。
    /// 同样既没有打印事件、也不是 RevealableText —— 玩家报告「笔记本还是没有朗读」。
    ///
    /// === 朗读策略（两块共用）===
    ///
    ///   · 只在面板**真的显示着**的时候工作（UiVis + 屏幕矩形）；
    ///   · 只念**此刻在画面矩形内**的文字：伪 ED 是整块在滚，
    ///     所以每一行是"滚进可见区"那一刻被念到的，节奏和画面一致；
    ///   · 同一行的多格（左/中/右）拼成一句念，不拆成三段；
    ///   · 只念一次（按实例 ID 记账），面板显示/隐藏都清账；
    ///   · 用「排队」而不是「打断」：一句接一句，打断会把上一句砍掉。
    ///
    /// 与对话朗读互不干扰：这些节点都不是 RevealableText，不会和打印器那条路重复。
    /// </summary>
    internal static class Subtitles
    {
        // ---------------- 字幕 ----------------

        private static SubtitleUI _ui;
        private static float _uiNextFind;
        private static bool _uiWarned;

        private static bool _wasPlaying;
        private static float _startedAt;
        private static float _nextScan;
        private static readonly HashSet<int> _said = new HashSet<int>();

        // ---------------- 笔记本（假回想）----------------

        private static Naninovel.UI.BackLogFakePanel _fake;
        private static float _fakeNextFind;
        private static bool _fakeWarned;

        private static bool _wasFakeOpen;
        private static float _fakeAt;
        private static float _nextFake;
        private static readonly HashSet<int> _fakeSaid = new HashSet<int>();

        private const float ScanInterval = 0.12f;   // ~8Hz 足够了
        private const float SettleSeconds = 0.45f;  // 刚打开时文本可能还没填进去

        /// <summary>取屏幕坐标用的角点缓冲。★ 必须是 Il2CppStructArray，托管数组写不回来。</summary>
        private static readonly Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3> _c =
            new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);

        public static void Update()
        {
            try { UpdateSubtitles(); }
            catch (Exception e) { Plugin.Diag("Subtitles: " + e.Message); }

            try { UpdateNotebook(); }
            catch (Exception e) { Plugin.Diag("Notebook: " + e.Message); }
        }

        // ================= 字幕 =================

        private static void UpdateSubtitles()
        {
            if (Plugin.CfgReadSubtitles != null && !Plugin.CfgReadSubtitles.Value) return;

            var ui = FindSubtitle();
            if (ui == null) return;

            bool playing;
            try { playing = ui.Playing; } catch { return; }

            if (playing && !_wasPlaying)
            {
                _said.Clear();
                _startedAt = Time.realtimeSinceStartup;
                Plugin.Diag("SUB 字幕开始播放");
            }
            else if (!playing && _wasPlaying)
            {
                _said.Clear();
                Plugin.Diag("SUB 字幕结束");
            }
            _wasPlaying = playing;
            if (!playing) return;

            if (Time.realtimeSinceStartup - _startedAt < SettleSeconds) return;
            if (Time.realtimeSinceStartup < _nextScan) return;
            _nextScan = Time.realtimeSinceStartup + ScanInterval;

            ReadPanel(ui.transform, _said, "SUB");
        }

        /// <summary>惰性定位 SubtitleUI；找不到只记一行日志，不拖垮别的功能。</summary>
        private static SubtitleUI FindSubtitle()
        {
            try
            {
                if (_ui != null) return _ui;

                // ★ 必须**反复重试**，不能"一次找不到就永久停用"（这里栽过一次）：
                //   插件是在游戏启动那一刻就被加载的，那时字幕面板/笔记本面板通常还不存在
                //   （Naninovel 按需实例化），一次找不到就放弃 = 这个功能永远不会生效。
                //   实测症状：日志里一句「没找到 BackLogFakePanel」，然后整局安静。
                if (Time.realtimeSinceStartup < _uiNextFind) return null;
                _uiNextFind = Time.realtimeSinceStartup + 2f;
                _ui = UnityEngine.Object.FindObjectOfType<SubtitleUI>();
                if (_ui == null && !_uiWarned)
                {
                    _uiWarned = true;
                    Plugin.Diag("SUB 暂未找到 SubtitleUI（没进字幕段之前属正常）");
                }
                return _ui;
            }
            catch (Exception e)
            {
                _uiWarned = true;
                Plugin.Diag("SUB 定位失败: " + e.GetType().Name);
                return null;
            }
        }

        // ================= 笔记本（假回想）=================

        private static void UpdateNotebook()
        {
            if (Plugin.CfgReadNotebook != null && !Plugin.CfgReadNotebook.Value) return;

            var panel = FindNotebook();
            if (panel == null) return;

            bool open = UiVis.Visible(panel);
            if (open && !_wasFakeOpen)
            {
                _fakeSaid.Clear();
                _fakeAt = Time.realtimeSinceStartup;
                Plugin.Diag("NOTE 笔记本打开 path=" + UiNav.PathOf(panel.transform));
            }
            else if (!open && _wasFakeOpen)
            {
                _fakeSaid.Clear();
                Plugin.Diag("NOTE 笔记本合上");
            }
            _wasFakeOpen = open;
            if (!open) return;

            if (Time.realtimeSinceStartup - _fakeAt < SettleSeconds) return;
            if (Time.realtimeSinceStartup < _nextFake) return;
            _nextFake = Time.realtimeSinceStartup + ScanInterval;

            ReadPanel(panel.transform, _fakeSaid, "NOTE");
        }

        private static Naninovel.UI.BackLogFakePanel FindNotebook()
        {
            try
            {
                if (_fake != null) return _fake;
                if (Time.realtimeSinceStartup < _fakeNextFind) return null;
                _fakeNextFind = Time.realtimeSinceStartup + 2f;
                _fake = UnityEngine.Object.FindObjectOfType<Naninovel.UI.BackLogFakePanel>();
                if (_fake != null)
                {
                    // 定位成功记一行 —— 排查时这一步和"面板没打开"必须能区分开
                    Plugin.Diag("NOTE 已定位笔记本面板 path=" + UiNav.PathOf(_fake.transform));
                }
                else if (!_fakeWarned)
                {
                    _fakeWarned = true;
                    Plugin.L.LogInfo("[a11y] 暂未找到 BackLogFakePanel（没进笔记本段之前属正常）");
                }
                return _fake;
            }
            catch (Exception e)
            {
                _fakeWarned = true;
                Plugin.Diag("NOTE 定位失败: " + e.GetType().Name);
                return null;
            }
        }

        // ================= 共用：念一块面板里"此刻看得见"的文字 =================

        private static void ReadPanel(Transform root, HashSet<int> said, string tag)
        {
            if (root == null) return;

            Rect view;
            bool hasView = TryScreenRect(root, out view);

            TMP_Text[] all;
            try { all = root.GetComponentsInChildren<TMP_Text>(true); }
            catch { return; }
            if (all == null || all.Length == 0) return;

            for (int i = 0; i < all.Length; i++)
            {
                TMP_Text t = all[i];
                if (t == null) continue;

                int id;
                try { id = t.GetInstanceID(); } catch { continue; }
                if (said.Contains(id)) continue;

                string body;
                try { body = TextProc.ToSpeech(t.text); } catch { continue; }
                if (string.IsNullOrEmpty(body)) continue;

                try { if (!t.gameObject.activeInHierarchy) continue; } catch { continue; }
                if (UiVis.Hidden(t.transform)) continue;
                if (hasView && !OnScreen(t.transform, view)) continue;

                // 同一行的多格（左/中/右）拼成一句念
                string line = GatherRow(t, view, hasView, said);
                if (string.IsNullOrEmpty(line)) { said.Add(id); continue; }

                Plugin.Diag(tag + " 念: " + line);
                Speech.Speak(line, false);
            }
        }

        /// <summary>把同一行里其它还没念过的格子拼进来（伪 ED 的一行是三格）。</summary>
        private static string GatherRow(TMP_Text self, Rect view, bool hasView, HashSet<int> said)
        {
            var sb = new StringBuilder();
            Transform parent = null;
            try { parent = self.transform.parent; } catch { }
            if (parent == null) return TextProc.ToSpeech(self.text);

            TMP_Text[] siblings;
            try { siblings = parent.GetComponentsInChildren<TMP_Text>(true); }
            catch { siblings = new TMP_Text[] { self }; }

            for (int i = 0; i < siblings.Length; i++)
            {
                TMP_Text s = siblings[i];
                if (s == null) continue;

                int sid;
                try { sid = s.GetInstanceID(); } catch { continue; }
                if (said.Contains(sid)) continue;

                string body;
                try { body = TextProc.ToSpeech(s.text); } catch { continue; }
                if (string.IsNullOrEmpty(body)) { said.Add(sid); continue; }

                try { if (!s.gameObject.activeInHierarchy) continue; } catch { continue; }
                if (UiVis.Hidden(s.transform)) continue;
                if (hasView && !OnScreen(s.transform, view)) continue;

                if (sb.Length > 0) sb.Append('，');
                sb.Append(body);
                said.Add(sid);
            }
            return sb.ToString();
        }

        /// <summary>面板当前占据的屏幕矩形（像素）。</summary>
        private static bool TryScreenRect(Transform t, out Rect r)
        {
            r = new Rect();
            try
            {
                RectTransform rt = UiNav.AsRect(t);
                if (rt == null) return false;
                r = ScreenRect(rt);
                return r.width > 1f && r.height > 1f;
            }
            catch { return false; }
        }

        /// <summary>某个文字节点是否落在面板的可见矩形内（按中心点判）。</summary>
        private static bool OnScreen(Transform t, Rect view)
        {
            try
            {
                RectTransform rt = UiNav.AsRect(t);
                if (rt == null) return true;
                Rect r = ScreenRect(rt);
                float cx = (r.xMin + r.xMax) * 0.5f;
                float cy = (r.yMin + r.yMax) * 0.5f;
                return cx >= view.xMin && cx <= view.xMax && cy >= view.yMin && cy <= view.yMax;
            }
            catch { return true; }
        }

        /// <summary>★ 必须走 Il2CppStructArray + WorldToScreenPoint，见 UiNav 里的两处踩坑记录。</summary>
        private static Rect ScreenRect(RectTransform rt)
        {
            rt.GetWorldCorners(_c);
            Vector2 a = RectTransformUtility.WorldToScreenPoint(null, _c[0]);
            Vector2 b = RectTransformUtility.WorldToScreenPoint(null, _c[1]);
            Vector2 c = RectTransformUtility.WorldToScreenPoint(null, _c[2]);
            Vector2 d = RectTransformUtility.WorldToScreenPoint(null, _c[3]);
            float minX = Mathf.Min(Mathf.Min(a.x, b.x), Mathf.Min(c.x, d.x));
            float maxX = Mathf.Max(Mathf.Max(a.x, b.x), Mathf.Max(c.x, d.x));
            float minY = Mathf.Min(Mathf.Min(a.y, b.y), Mathf.Min(c.y, d.y));
            float maxY = Mathf.Max(Mathf.Max(a.y, b.y), Mathf.Max(c.y, d.y));
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }
    }
}