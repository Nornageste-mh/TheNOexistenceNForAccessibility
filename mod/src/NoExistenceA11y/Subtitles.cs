using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace NoExistenceA11y
{
    /// <summary>
    /// 字幕（本作剧本里的 `@subtitle` 命令）朗读。
    ///
    /// === 为什么必须单独接一条路 ===
    ///
    /// 本作的片尾字幕**不走对话打印器**。剧本里只有一句
    /// `@subtitle FakeED`（Assembly-CSharp 里的 CustomSubtitle 命令），
    /// 画面由 SubtitleUI 播一段 Animation 完成：
    ///
    ///   SubtitleUI
    ///     VocalConcert/01..13          ← 唱歌那段，一行一句
    ///     FakeED/node/1..31/{left,middle,right}  + CAST / STAFF / END
    ///                                  ← 「伪 ED」，整块 node 往上滚
    ///     ED/right/01..10/…            ← 真 ED，一屏一屏地出现
    ///
    /// 文字是**预制体里的 TMP 节点**，由 ManagedTextProvider 在运行期填本地化文本，
    /// 没有任何 RevealableText、也没有 OnPrintTextStarted 事件。
    /// 补丁原来只挂了 RevealableText.set_Text + 打印事件，所以这一段**一个字都不念** ——
    /// 玩家报告的原话就是「第四章结尾的字幕不可读」。
    ///
    /// === 朗读策略：跟着画面走，不是一次性倒出来 ===
    ///
    ///   · 只在 SubtitleUI 真的在播（Playing）时工作；
    ///   · 只念**此刻在画面矩形内**的文字：伪 ED 是整块在滚，
    ///     所以每一行是「滚进可见区」的那一刻被念到的，节奏和画面一致；
    ///   · 同一行的多格（左/中/右）拼成一句念，不拆成三段；
    ///   · 只念一次（按实例 ID 记账），播放开始/结束都会清账；
    ///   · 用「排队」而不是「打断」：字幕一句接一句，打断会把上一句砍掉。
    ///
    /// 与对话朗读互不干扰：字幕节点不是 RevealableText，不会被打印器那条路重复念。
    /// </summary>
    internal static class Subtitles
    {
        private static SubtitleUI _ui;
        private static bool _finding;
        private static bool _played;

        private static bool _wasPlaying;
        private static float _startedAt;
        private static float _nextScan;

        /// <summary>本次播放里已经念过的文字节点（实例 ID）。</summary>
        private static readonly HashSet<int> _said = new HashSet<int>();

        private const float ScanInterval = 0.12f;   // ~8Hz 足够了
        private const float SettleSeconds = 0.45f;  // 播放刚开始时文本可能还没填进去

        /// <summary>取屏幕坐标用的角点缓冲。★ 必须是 Il2CppStructArray，托管数组写不回来。</summary>
        private static readonly Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3> _c =
            new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);

        public static void Update()
        {
            if (Plugin.CfgReadSubtitles != null && !Plugin.CfgReadSubtitles.Value) return;

            try
            {
                var ui = Find();
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

                Read(ui);
            }
            catch (Exception e)
            {
                Plugin.Diag("Subtitles: " + e.Message);
            }
        }

        /// <summary>惰性定位 SubtitleUI；找不到只记一行日志，不拖垮别的功能。</summary>
        private static SubtitleUI Find()
        {
            try
            {
                if (_ui != null) return _ui;
                if (_finding) return null;
                _finding = true;
                _ui = UnityEngine.Object.FindObjectOfType<SubtitleUI>();
                if (_ui == null && !_played)
                {
                    Plugin.L.LogWarning("[a11y] 没找到 SubtitleUI，字幕朗读停用（游戏改版？）");
                    Plugin.Diag("SUB 找不到 SubtitleUI");
                    _played = true;
                }
                return _ui;
            }
            catch (Exception e)
            {
                _finding = false;
                Plugin.Diag("SUB 定位失败: " + e.GetType().Name);
                return null;
            }
        }

        private static void Read(SubtitleUI ui)
        {
            Rect view;
            bool hasView = TryScreenRect(ui.transform, out view);

            TMP_Text[] all;
            try { all = ui.GetComponentsInChildren<TMP_Text>(true); }
            catch { return; }
            if (all == null || all.Length == 0) return;

            for (int i = 0; i < all.Length; i++)
            {
                TMP_Text t = all[i];
                if (t == null) continue;

                int id;
                try { id = t.GetInstanceID(); } catch { continue; }
                if (_said.Contains(id)) continue;

                string body;
                try { body = TextProc.ToSpeech(t.text); } catch { continue; }
                if (string.IsNullOrEmpty(body)) continue;

                try { if (!t.gameObject.activeInHierarchy) continue; } catch { continue; }
                if (UiVis.Hidden(t.transform)) continue;
                if (hasView && !OnScreen(t.transform, view)) continue;

                // 同一行的多格（左/中/右）拼成一句念
                string line = GatherRow(t, view, hasView);
                if (string.IsNullOrEmpty(line)) { _said.Add(id); continue; }

                Plugin.Diag("SUB 念: " + line);
                Speech.Speak(line, false);
            }
        }

        /// <summary>把同一行里其它还没念过的格子拼进来（伪 ED 的一行是三格）。</summary>
        private static string GatherRow(TMP_Text self, Rect view, bool hasView)
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
                if (_said.Contains(sid)) continue;

                string body;
                try { body = TextProc.ToSpeech(s.text); } catch { continue; }
                if (string.IsNullOrEmpty(body)) { _said.Add(sid); continue; }

                try { if (!s.gameObject.activeInHierarchy) continue; } catch { continue; }
                if (UiVis.Hidden(s.transform)) continue;
                if (hasView && !OnScreen(s.transform, view)) continue;

                if (sb.Length > 0) sb.Append('，');
                sb.Append(body);
                _said.Add(sid);
            }
            return sb.ToString();
        }

        /// <summary>字幕面板当前占据的屏幕矩形（像素）。</summary>
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

        /// <summary>某个文字节点是否落在字幕面板的可见矩形内（按中心点判）。</summary>
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