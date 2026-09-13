using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NoExistenceA11y
{
    /// <summary>
    /// QTE（烤箱那段）自动通过。
    ///
    /// 为什么必须绕过而不是「朗读提示」：这段是**纯视觉**的反应按键——没有节奏音、
    /// 没有任何可听的线索，按钮自己出现自己消失，而且**得分低于 60% 会无限重来**，
    /// 是一道硬门槛。盲人玩家不是「玩得差」，是根本没有可玩的通道。
    ///
    /// 做法：QTE 面板活着的时候，每帧把它里面所有「激活且可点」的按钮点一遍。
    /// 这样每次判定都落在按钮存续期内，稳定拿满，剧情照常往下走。
    ///
    /// 不去引用 Assembly-CSharp 里的 QTEUI / QTEButton 类型 —— 靠对象名找面板、
    /// 靠 Button 组件点击，游戏改版时不会因为字段改名而整个插件加载失败。
    /// </summary>
    internal static class Qte
    {
        private static GameObject _panel;
        private static int _rescanAt;
        private static bool _announced;
        private static float _lastClickAt;
        private static int _clicks;
        private static float _sessionStart;

        private const string PanelName = "QTEPanel";

        private static GameObject Panel()
        {
            if (_panel != null && _panel) return _panel;
            if (Time.frameCount < _rescanAt) return null;
            _rescanAt = Time.frameCount + 30;          // 半秒找一次，不必每帧
            try
            {
                _panel = GameObject.Find(PanelName);
                if (_panel == null)
                {
                    // 名字变了就按 QTE 关键字兜底找一次
                    var all = Resources.FindObjectsOfTypeAll<Transform>();
                    foreach (var t in all)
                    {
                        if (t == null || t.gameObject == null) continue;
                        if (t.name.IndexOf("QTE", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (t.parent != null && t.parent.name.IndexOf("QTE", StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;                       // 只要最外层那个
                        _panel = t.gameObject;
                        break;
                    }
                }
            }
            catch { }
            return _panel;
        }

        internal static void Update()
        {
            if (Plugin.CfgQteAutoPass == null || !Plugin.CfgQteAutoPass.Value) return;

            var panel = Panel();
            if (panel == null) { _announced = false; return; }

            bool live;
            try { live = panel.activeInHierarchy; } catch { live = false; }
            if (!live)
            {
                if (_announced)
                {
                    Plugin.Diag("QTE 结束，共点了 " + _clicks + " 次");
                    _announced = false;
                    _clicks = 0;
                }
                return;
            }

            if (!_announced)
            {
                _announced = true;
                _clicks = 0;
                _sessionStart = Time.realtimeSinceStartup;
                Plugin.Diag("QTE 开始，自动通过中");
                Speech.Speak("反应环节，已自动通过。", true);
            }

            // 每帧都点：按钮一出现就被按下，不会漏
            int n = 0;
            try
            {
                var btns = panel.GetComponentsInChildren<Button>(false);
                if (btns != null)
                {
                    foreach (var b in btns)
                    {
                        if (b == null || b.gameObject == null) continue;
                        if (!b.gameObject.activeInHierarchy) continue;
                        if (!b.interactable) continue;
                        b.onClick.Invoke();
                        n++;
                    }
                }
            }
            catch (Exception e) { Plugin.Diag("Qte.Update: " + e.Message); }

            if (n > 0)
            {
                _clicks += n;
                _lastClickAt = Time.realtimeSinceStartup;
                if (_clicks <= 6 || _clicks % 20 == 0)
                    Plugin.Diag("QTE 点击 x" + n + "（累计 " + _clicks + "）");
            }
        }
    }
}
