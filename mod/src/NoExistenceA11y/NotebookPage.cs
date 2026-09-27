using System;

namespace NoExistenceA11y
{
    /// <summary>
    /// 笔记本「纸页」的播报（作者点名要的那一条：显示笔记本页时念「笔记本图像」）。
    ///
    /// === 纸页是图片，不是文本 ===
    ///
    /// 剧情里翻开的笔记本是**图片**：剧本用一句 `@ModifyBackground diary1`
    /// （`diary1`~`diary4` 四个资源，Prologue4_5 里各一次）把整页纸换上来，
    /// 后面跟一个 `@WaitForInput` —— 一页一页翻。
    /// 纸页上的手写字（页码 23/63、涂鸦）**在游戏数据里根本不存在**：
    /// 实测把 data.unity3d 里 4945 个文本对象逐个扫、再把
    /// data.unity3d(417MB) + resources.resource(2142MB) 按原始字节扫，特征短语 0 命中。
    /// 字是画进图里的，**任何读文本的钩子都拿不到**。
    ///
    /// 所以补丁能做、也只该做的，是**如实告诉玩家「这是一张图」**。
    /// 补丁**不会**把纸页原文抄进来当查表 —— 那是游戏的美术与文本，
    /// 抄进仓库就违背了本项目「不含任何游戏资源」的底线。
    ///
    /// === 为什么用轮询而不是 Harmony 补丁 ===
    ///
    /// 一开始挂的是 `@ModifyBackground` 命令（`ExecuteAsync`），结果实机日志明说挂不上：
    ///     AccessTools.DeclaredMethod: Could not find method for type
    ///     Naninovel.Commands.ModifyBackground and name ExecuteAsync
    /// —— 那个方法声明在**泛型基类** `ModifyOrthoActor&lt;…&gt;` 上，PatchAll 不会往上找。
    /// 改成每 0.5 秒问一次引擎：**背景演员当前挂的是哪张图**（`IActor.Appearance`），
    /// 名字里有 `diary` 就报一声。两个 interop 调用，代价可以忽略，也不动游戏代码。
    /// </summary>
    internal static class NotebookPage
    {
        /// <summary>上一次报过的页（避免同一页重复念）。</summary>
        private static string _last;
        private static float _lastAt;
        private static float _next;
        private static bool _warned;

        /// <summary>每 0.5 秒看一眼背景演员（由 A11yBehaviour.Update 调用）。</summary>
        internal static void Update()
        {
            if (Plugin.CfgAnnounceNotebookImage != null && !Plugin.CfgAnnounceNotebookImage.Value) return;

            try
            {
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (now < _next) return;
                _next = now + 0.5f;

                if (!Naninovel.Engine.Initialized) return;

                // ★ IBackgroundManager 这个**接口代理**上没有 GetActor（它声明在基接口 IActorManager 上，
                //   Il2CppInterop 的接口代理只暴露该接口自己的成员）—— 必须 TryCast 到基接口再调。
                var mgr = Naninovel.Engine.GetService<Naninovel.IBackgroundManager>();
                if (mgr == null) return;

                var actors = ((Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)mgr)
                    .TryCast<Naninovel.IActorManager>();
                if (actors == null) return;

                // `@ModifyBackground diary1` 没写演员 id，改的就是默认背景演员（id = Background）。
                // ★ GetActor 在"演员还没建出来"时会抛（实测日志里的 NB 轮询背景失败: Il2CppException），
                //   所以这一句必须单独兜住：报一次原因就够，别每 0.5 秒刷一次。
                Naninovel.IActor actor = null;
                try { actor = actors.GetActor("Background"); }
                catch (Exception e)
                {
                    if (!_warned)
                    {
                        _warned = true;
                        Plugin.Diag("NB 背景演员暂时取不到（未进剧情？）: " + e.Message);
                    }
                    return;
                }
                if (actor == null) return;

                string appearance = null;
                try { appearance = actor.Appearance; } catch { }
                MaybeAnnounce(appearance);
            }
            catch (Exception e)
            {
                if (!_warned)
                {
                    _warned = true;
                    Plugin.Diag("NB 轮询背景失败: " + e.GetType().Name);
                }
            }
        }

        /// <summary>名字里带 diary 就当成笔记本页（不做精确比较，免得被包装形式坑到）。</summary>
        internal static void MaybeAnnounce(string appearance)
        {
            if (string.IsNullOrEmpty(appearance)) return;

            bool hit = false;
            try { hit = appearance.IndexOf("diary", StringComparison.OrdinalIgnoreCase) >= 0; }
            catch { }
            if (!hit) return;

            // 同一页重复上报就不再念（翻页时页面名会变，所以正常翻页照样念）
            if (_last == appearance && UnityEngine.Time.realtimeSinceStartup - _lastAt < 2f) return;
            _last = appearance;
            _lastAt = UnityEngine.Time.realtimeSinceStartup;

            Plugin.Diag("NB 笔记本图像：" + appearance);
            Speech.Speak("笔记本图像", true);
        }
    }
}
