using UnityEngine;

namespace NoExistenceA11y
{
    /// <summary>
    /// 「这块 UI 此刻到底在不在画面上」的统一判定。
    ///
    /// 为什么需要单独一个：Naninovel 的面板靠 `CanvasGroup` 做显隐 ——
    /// 面板藏起来时 GameObject **仍然是 active**，`blocksRaycasts` 仍然是 true，
    /// UI 射线照样命中。所以 `activeInHierarchy` 和射线都判不出「隐藏」，
    /// 必须显式查 CanvasGroup。
    ///
    /// 这个判定的三处用途，每一处都对应一次实机事故：
    ///   1. 导航扫描（UiNav）—— 不查就会把隐藏面板的控件变成可导航、可点击，
    ///      玩家能翻进剧透面板，也能点中当前状态下不该点的控件把游戏搞坏。
    ///   2. 文本朗读（Plugin.OnReveal）—— 不查就会把「写进隐藏面板、玩家看不到」
    ///      的文本念出来，实测在载入存档时念出了结局文本。
    ///   3. 选项朗读（Choices）—— 不查就会把上一批还留在池子里的选项当成新选项念。
    ///
    /// 判不出来时一律返回「不隐藏」：漏读一句是功能缺失，误读是事故。
    /// </summary>
    internal static class UiVis
    {
        private const int MaxDepth = 40;

        /// <summary>沿父链查 CanvasGroup；alpha≈0 / interactable=false / blocksRaycasts=false 即视为不可见。</summary>
        public static bool Hidden(Transform t)
        {
            if (t == null) return false;
            try
            {
                Transform cur = t;
                int guard = 0;
                while (cur != null && guard++ < MaxDepth)
                {
                    CanvasGroup cg = null;
                    try { cg = cur.GetComponent<CanvasGroup>(); } catch { }
                    if (cg != null)
                    {
                        if (cg.alpha < 0.01f) return true;
                        if (!cg.interactable) return true;
                        if (!cg.blocksRaycasts) return true;
                    }
                    cur = cur.parent;
                }
            }
            catch { }
            return false;
        }

        /// <summary>组件所在物体此刻是否真的显示在画面上。</summary>
        public static bool Visible(Component c)
        {
            if (c == null) return false;
            try
            {
                var go = c.gameObject;
                if (go == null || !go.activeInHierarchy) return false;
                return !Hidden(c.transform);
            }
            catch { return false; }
        }
    }
}
