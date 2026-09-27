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
        private static float _next;
        private static float _nextScan;
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
                // 演员 id 从游戏资源里问出来的：本作 BackgroundsConfiguration 里配的是
                // **MainBackground**（不是 Naninovel 默认的 "Background" —— 实测报
                //  Naninovel.Error: Can't find 'Background' actor.）。两个都试，再兜画面扫描。
                Naninovel.IActor actor = null;
                Exception last = null;
                foreach (string id in new[] { "MainBackground", "Background" })
                {
                    try { actor = actors.GetActor(id); } catch (Exception e) { last = e; actor = null; }
                    if (actor != null) break;
                }
                if (actor == null && !_warned)
                {
                    _warned = true;
                    Plugin.Diag("NB 背景演员取不到（" + (last != null ? last.Message : "null") + "），改用画面扫描");
                }

                if (actor != null)
                {
                    string appearance = null;
                    try { appearance = actor.Appearance; } catch { }
                    MaybeAnnounce(appearance, "演员");
                    return;
                }

                // 退路：直接看有没有哪个 UI 组件正挂着 diaryN 的精灵/贴图。
                // 代价比读演员状态大，所以 2 秒一次。
                if (now < _nextScan) return;
                _nextScan = now + 2f;
                ScanSpriteNames();
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

        /// <summary>
        /// 退路：扫画面上的 Image / RawImage / SpriteRenderer，看有没有挂着 diaryN。
        /// 只在读不到背景演员时才走这条路。
        /// </summary>
        private static void ScanSpriteNames()
        {
            string hit = null;
            try
            {
                // ★ FindObjectsOfTypeAll 会把**资源里加载着、但根本没显示**的对象也返回
                //   （实机症状：笔记本早就翻完了，屏幕上没有任何日记页，却每 2 秒报一次 diary4）。
                //   所以这里必须逐项确认：在有效场景里、激活、且没被 CanvasGroup 藏起来。
                var imgs = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.UI.Image>();
                for (int i = 0; i < imgs.Length && hit == null; i++)
                {
                    var im = imgs[i];
                    if (im == null || im.sprite == null) continue;
                    if (!Live(im)) continue;
                    string n = null;
                    try { n = im.sprite.name; } catch { }
                    if (LooksLikeDiary(n)) hit = n;
                }

                if (hit == null)
                {
                    var raws = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.UI.RawImage>();
                    for (int i = 0; i < raws.Length && hit == null; i++)
                    {
                        var r = raws[i];
                        if (r == null || r.texture == null) continue;
                        if (!Live(r)) continue;
                        string n = null;
                        try { n = r.texture.name; } catch { }
                        if (LooksLikeDiary(n)) hit = n;
                    }
                }

                if (hit == null)
                {
                    var sprs = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.SpriteRenderer>();
                    for (int i = 0; i < sprs.Length && hit == null; i++)
                    {
                        var s = sprs[i];
                        if (s == null || s.sprite == null) continue;
                        if (!Live(s)) continue;
                        string n = null;
                        try { n = s.sprite.name; } catch { }
                        if (LooksLikeDiary(n)) hit = n;
                    }
                }
            }
            catch (Exception e)
            {
                if (!_warned) { _warned = true; Plugin.Diag("NB 画面扫描失败: " + e.GetType().Name); }
                return;
            }

            if (hit != null) MaybeAnnounce(hit, "画面");
        }

        /// <summary>
        /// 这个组件此刻是不是真的"在画面上"：在有效场景里 + 激活 + 没被 CanvasGroup 藏起来。
        /// （资源对象、隐藏面板里的对象一律不算 —— 见上面那段踩坑记录。）
        /// </summary>
        private static bool Live(UnityEngine.Component c)
        {
            try
            {
                var go = c.gameObject;
                if (go == null || !go.activeInHierarchy) return false;
                if (!go.scene.IsValid()) return false;      // 资源/prefab，不是场景里的
                return !UiVis.Hidden(c.transform);
            }
            catch { return false; }
        }

        private static bool LooksLikeDiary(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            try { return name.IndexOf("diary", StringComparison.OrdinalIgnoreCase) >= 0; }
            catch { return false; }
        }

        /// <summary>名字里带 diary 就当成笔记本页（不做精确比较，免得被包装形式坑到）。</summary>
        internal static void MaybeAnnounce(string appearance, string source)
        {
            // 不是日记页就清掉记忆：下次再翻到同一页还会念
            if (!LooksLikeDiary(appearance)) { _last = null; return; }

            // ★ **只在值变化时念**，绝不能按时间重复。
            //   踩过的坑：原来写的是"同一个值 2 秒内不重复"，结果背景演员的 Appearance
            //   在翻完日记页之后**一直停在 diary4**，于是每 2 秒念一次「笔记本图像」——
            //   实机日志里 102 行重复（玩家反馈："总是在不该出现的时候反复朗读"）。
            if (_last == appearance) return;
            _last = appearance;

            Plugin.Diag("NB 笔记本图像（" + source + "）：" + appearance);
            Speech.Speak("笔记本图像", true);
        }
    }
}
