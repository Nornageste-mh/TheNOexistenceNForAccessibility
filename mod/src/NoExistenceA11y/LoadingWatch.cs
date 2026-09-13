using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NoExistenceA11y
{
    /// <summary>
    /// 朗读「正在加载」。
    ///
    /// 为什么需要：读屏玩家在加载期间完全不知道游戏在干什么 —— 没有画面可看，
    /// 也没有声音线索，很容易以为卡死了然后去乱按。一句「正在加载」就能解决。
    ///
    /// 两条通路都要盯，因为本作两种加载都有：
    ///   1. Naninovel 的 LoadingPanel（剧本预载、资源加载）
    ///   2. Unity 自己的场景切换（游戏有一张专门的加载场景，名字里带 load）
    ///
    /// 判可见性走 UiVis —— 和导航、朗读同一套标准（CanvasGroup）。
    /// 面板只是被实例化但没显示时不应该播报。
    /// </summary>
    internal static class LoadingWatch
    {
        private static Naninovel.UI.LoadingPanel _panel;
        private static int _rescanAt;
        private static bool _panelVisible;
        private static bool _typeBroken;

        private static string _lastScene = "";
        private static bool _sceneAnnounced;
        private static float _lastAnnounce = -999f;

        /// <summary>同一波加载里只播一次，避免面板闪烁时反复念。</summary>
        private const float Debounce = 3f;

        private static void Announce(string why)
        {
            if (Plugin.CfgAnnounceLoading == null || !Plugin.CfgAnnounceLoading.Value) return;
            if (Time.realtimeSinceStartup - _lastAnnounce < Debounce) return;
            _lastAnnounce = Time.realtimeSinceStartup;
            Plugin.Diag("加载播报（" + why + "）");

            // ★ 必须**打断**，不能排队。
            //
            // 之前这里用的是 false（不打断），结果玩家点了「开始游戏」之后
            // 半天听不到任何反馈 —— 因为这句「正在加载」排在上一句台词的后面，
            // 而上一句可能还要念好几秒。玩家的第一反应是「补丁坏了 / 我点错了」，
            // 然后开始乱按，反而把状态搞乱。
            //
            // 加载提示回答的正是「为什么现在没反应」，时效性就是它的全部价值。
            // 被它打断的那句可以按退格重读，代价很小。
            Speech.Speak("正在加载。", true);
        }

        private static Naninovel.UI.LoadingPanel Find()
        {
            try
            {
                if (_panel != null && _panel) return _panel;
                if (Time.frameCount < _rescanAt) return null;
                _rescanAt = Time.frameCount + 30;
                _panel = UnityEngine.Object.FindObjectOfType<Naninovel.UI.LoadingPanel>();
                return _panel;
            }
            catch (Exception e)
            {
                if (!_typeBroken)
                {
                    _typeBroken = true;
                    Plugin.L.LogWarning("[a11y] LoadingPanel 类型不可用，加载播报降级为只认场景名：" + e.GetType().Name);
                }
                return null;
            }
        }

        internal static void Update()
        {
            if (Plugin.CfgAnnounceLoading == null || !Plugin.CfgAnnounceLoading.Value) return;

            // ---- 1) Naninovel 的加载面板 ----
            if (!_typeBroken)
            {
                var p = Find();
                if (p != null)
                {
                    bool vis = UiVis.Visible(p);
                    if (vis && !_panelVisible) Announce("LoadingPanel");
                    _panelVisible = vis;
                }
            }

            // ---- 2) Unity 场景切换 ----
            try
            {
                string sc = SceneManager.GetActiveScene().name;
                if (sc != _lastScene)
                {
                    _lastScene = sc;
                    _sceneAnnounced = false;
                }
                if (!_sceneAnnounced && !string.IsNullOrEmpty(sc)
                    && sc.IndexOf("load", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _sceneAnnounced = true;
                    Announce("场景 " + sc);
                }
            }
            catch { }
        }
    }
}
