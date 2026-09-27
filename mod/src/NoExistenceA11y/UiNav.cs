using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NoExistenceA11y
{
    /// <summary>
    /// 菜单 / 存档 / 设置等界面的键盘导航与朗读。
    ///
    ///   Tab          进入 / 退出导航模式
    ///   上 / 下      上一项 / 下一项
    ///   左 / 右      调整滑条
    ///   回车 / 空格  激活（按钮点击、开关切换、输入框聚焦）
    ///   Home / End   第一项 / 最后一项
    ///   PageUp/PageDown  切换面板组（默认只导航最上层那一组）
    ///
    /// === 回车 / 空格的归属（状态机，见 F）===
    ///
    ///   非导航模式                      → 不归我们，交给游戏推进剧情
    ///   导航模式 + 当前没有可用控件      → 退出导航，交回游戏推进剧情
    ///   导航模式 + 当前有可用控件        → 归我们，激活该控件
    ///
    ///   判定点是「本帧开始时导航模式是否有效且选中项还活着」，
    ///   而不是「EventSystem 里有没有选中对象」—— 后者会被鼠标点击和
    ///   游戏的 AutoFocusInputField 干扰，不能用来决定按键归属。
    ///
    /// === 设计约束（每条都对应一次实测故障）===
    ///
    /// A) 【只导航最上层面板组】
    ///    实测存档界面一次能扫到 13~20 个控件，其中只有 5 个是存档槽，
    ///    其余是设置页签、画廊等无关按钮，读屏用户要在一堆噪声里找目标。
    ///    现在按「Canvas 下的顶层祖先」分组，默认只进入渲染层级最高的
    ///    那一组，用 PageUp/PageDown 手动切换。
    ///
    /// B) 【排序按完整渲染路径，而不是只看屏幕坐标】
    ///    确认框是覆盖层：LoadSlotUI.OnSaveSlotClicked 直接
    ///    confirmLoadPanel.SetActive(true)，背后存档槽按钮仍然 active。
    ///    只按坐标排会让弹窗按钮混在底层按钮中间。
    ///    现在按兄弟序号路径逐级比较（Unity UI 中后渲染的在上层）。
    ///
    /// C) 【标签必须拼接多个文本】
    ///    SaveSlot 有 saveNameText / saveTimeText / chapterText 三个字段，
    ///    只取「子物体里第一个 TMP」会让 5 个空槽位读出完全相同的
    ///    「无存档」，用户无法区分，表现就像「每页只能选一个槽」。
    ///    现在把不同的文本拼起来（最多 3 段）。
    ///
    /// D) 【绝不定时重扫 + 场景切换必须复位】
    ///    v0.3.0 曾在激活按钮后 0.35 秒重扫界面。「开始游戏」「读档」会立刻
    ///    SceneManager.LoadSceneAsync，重扫恰好落在场景销毁/激活瞬间，
    ///    对正在销毁的 Selectable 调用 FindObjectsOfTypeAll 会触发
    ///    无 C# 异常的原生崩溃。
    ///    现在只在激活后的「下一帧」重扫（旧场景仍完整存活），
    ///    且必须通过 SceneStable 门禁。
    ///
    /// E) 【恢复视觉反馈】
    ///    用 EventSystem.SetSelectedGameObject 让游戏自己的按钮高亮态生效。
    ///
    /// F) 【按键归属必须是显式的，不能靠「有没有选中对象」推断】
    ///    实测：退出导航模式后按空格推进剧情，会把上一次导航到的那个按钮
    ///    又点一遍。原因是 EventSystem.currentSelectedGameObject 在我们退出后
    ///    仍然指着那个按钮，而 StandaloneInputModule 一见回车/空格就向它发
    ///    submit。鼠标点过的按钮同理（Selectable.OnPointerDown 会自动选中自己）。
    ///
    ///    现在改成两件事：
    ///      1) 常驻把 EventSystem.sendNavigationEvents 置 false（见 KeepUnitySubmitOff），
    ///         让 uGUI 那条 submit 通路彻底不存在；
    ///      2) 回车/空格在我们自己手里时才算「提交」（见 Update 里的状态机），
    ///         并且同一帧拦住游戏自身那次多余的推进（见 BlockGameAdvance）。
    ///
    ///    为什么常驻关闭 submit 是安全的：全游戏代码里没有任何一处
    ///    SetSelectedGameObject，游戏自己并不依赖 EventSystem 选中态；
    ///    它的剧情推进、输入框确认、Esc 返回全部是自己读 Input.GetKeyDown。
    ///    鼠标点按走 pointer 事件，不受这个开关影响。
    ///    游戏自己播视频时也用同一个开关关掉提交（VideoPanelManager）。
    /// </summary>
    internal static class UiNav
    {
        private class Group
        {
            public Transform Root;
            public int CanvasOrder;
            public int SiblingIndex;
            public readonly List<Component> Items = new List<Component>();

            /// <summary>
            /// 游戏自己判定「此刻不可用」（interactable = false）的控件。
            /// 平时不进导航；只有当这一组一个可用控件都不剩时才按「只读」纳入
            /// （见 Scan 里的兜底），念得出来、但**永远不激活**。
            /// </summary>
            public readonly List<Component> ReadOnly = new List<Component>();

            /// <summary>
            /// 「游戏标成可用、这一帧却点不到」的控件（典型：彩蛋设置界面里的**返回**按钮 ——
            /// `interactable = true`，但父级 CanvasGroup 关了交互，严格可见性判定会把它
            /// 判成"画面外或点不到"）。
            /// 与 ReadOnly 一样，只在整组一个能按的都不剩时才启用；它们本身是可用的，
            /// 所以收回来之后**照常可以激活** —— 那正是玩家要的（不然就出不去了）。
            /// </summary>
            public readonly List<Component> Blocked = new List<Component>();

            // 本组所在 Canvas 的世界矩形与相机：用来判断成员是不是真的在画面上
            public Rect CanvasRect;
            public bool HasCanvasRect;
            public Camera Camera;
        }

        /// <summary>
        /// IL2CPP 下**不能**用 `as` / `is` 做 Unity 组件类型判断。
        /// Il2CppInterop 是按「方法的声明返回类型」造代理对象的，所以
        /// `s.transform` 拿到的是一个 Transform 代理，哪怕底层其实是 RectTransform，
        /// `s.transform is RectTransform` 也永远是 false，`as` 永远是 null ——
        /// 而且是静默失败，不报错，表现就是「界面上明明有控件，一个都扫不到」。
        /// 必须走 TryCast，它按原生类做兼容性判断。
        /// </summary>
        private static T As<T>(UnityEngine.Object o) where T : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
        {
            if (o == null) return null;
            try { return ((Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)o).TryCast<T>(); }
            catch { return null; }
        }
        /// <summary>取 Transform 上的 RectTransform。IL2CPP 下 `is`/`as` 恒为假，必须 TryCast（见 As<T>）。</summary>
        internal static RectTransform AsRect(Transform t)
        {
            if (t == null) return null;
            try { return As<RectTransform>(t); } catch { return null; }
        }

        private static readonly List<Group> Groups = new List<Group>();
        private static int _groupIndex;
        private static List<Component> Items { get { return Groups[_groupIndex].Items; } }

        private static bool _active;
        private static int _index;
        private static int _pendingRescanFrame = -1;
        // 稍晚再补一次重扫：面板常常有滑入/淡入动画，下一帧新控件可能还没激活
        private static int _pendingRescanFrame2 = -1;

        // 视觉反馈用：我们自己借 EventSystem 选中过的对象，退出时要清掉
        private static GameObject _selectedByUs;

        // 「提交键」状态机：本帧是否已由我们处理，以及是否正在执行我们发起的动作
        private static int _submitHandledFrame = -1;
        private static bool _inOurActivation;

        // 上一次朗读过的控件。重扫后如果这个位置换了别的控件，必须重新播报 ——
        // 否则玩家以为还停在刚才听的那一项上，按下去却是另一个东西。
        private static Component _announcedItem;

        // 当前项失效时我们请求过一次重扫，记下来免得每帧都重扫
        private static Component _rescanRequestedFor;

        // 「当前项已经死掉」这类失效的重扫节流（见 Update 里那一段）
        private static float _nextDeadRescan;

        // 场景切换防护
        private static int _lastSceneHandle = int.MinValue;
        private static float _sceneChangedAt = float.NegativeInfinity;
        private const float SceneSettleSeconds = 1.5f;

        public static bool Active { get { return _active; } }

        // ================= 进入 / 退出 =================

        public static void Toggle()
        {
            if (_active) ExitInternal(true);
            else Enter();
        }

        private static void Enter()
        {
            if (!SceneStable())
            {
                Speech.Speak("场景正在切换，请稍候再试。", true);
                return;
            }

            Scan();
            if (Groups.Count == 0 || Items.Count == 0)
            {
                Speech.Speak("当前界面上没有可操作的项目。", true);
                return;
            }

            _active = true;
            _index = 0;
            BlockGameInput(true);
            AnnounceGroup(true);
        }

        private static void ExitInternal(bool announce)
        {
            _active = false;
            Groups.Clear();
            _index = 0;
            _pendingRescanFrame = -1;
            _announcedItem = null;
            ClearQuitConfirm();
            ReleaseSelection();
            BlockGameInput(false);
            if (announce)
            {
                try { Speech.Speak("已退出导航模式。", true); } catch { }
            }
        }

        // ================= 导航模式下屏蔽游戏自身的输入 =================

        private static bool _inputBlocked;
        private static bool _prevProcessInput = true;

        /// <summary>
        /// 导航模式期间把 Naninovel 的输入总开关关掉。
        ///
        /// 为什么需要：回车/空格同时是「激活当前控件」和「推进剧情」。
        /// 不屏蔽的话，在回想面板里按回车重念一句，剧情会跟着往前走一格 ——
        /// 玩家只是想重听，结果位置变了。
        ///
        /// 用的是引擎自己的接口 `IInputManager.ProcessInput`，不是去 patch 游戏代码：
        ///   · 它是官方提供的「暂停处理输入」总闸，语义正好
        ///   · 完全可逆，退出导航就还原
        ///   · 进入时**记下当时的值**再改，退出时还原成记下的那个值 ——
        ///     这样不会覆盖游戏自己因为别的原因（比如播片）关掉的输入
        /// </summary>
        private static void BlockGameInput(bool block)
        {
            try
            {
                if (Plugin.CfgNavBlockInput != null && !Plugin.CfgNavBlockInput.Value) return;

                var im = Naninovel.Engine.GetService<Naninovel.IInputManager>();
                if (im == null) return;

                if (block)
                {
                    if (_inputBlocked) return;
                    _prevProcessInput = im.ProcessInput;
                    im.ProcessInput = false;
                    _inputBlocked = true;
                    Plugin.Diag("导航模式：已屏蔽游戏输入（原值 " + _prevProcessInput + "）");
                }
                else
                {
                    if (!_inputBlocked) return;
                    im.ProcessInput = _prevProcessInput;
                    _inputBlocked = false;
                    Plugin.Diag("导航模式：已还原游戏输入（" + _prevProcessInput + "）");
                }
            }
            catch (Exception e)
            {
                _inputBlocked = false;
                Plugin.Diag("屏蔽游戏输入失败: " + e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>
        /// 常驻关掉 uGUI 的键盘导航/提交通路。
        ///
        /// 为什么是常驻而不是「只在导航模式里」：
        ///   游戏的推进剧情是自己在 Update 里读 Input.GetKeyDown(空格/回车) 的。
        ///   而 StandaloneInputModule 也会在回车/空格时向「当前选中对象」发一次
        ///   submit。只要之前有任何控件被选中过——鼠标点过、游戏自己的
        ///   AutoFocusInputField.Select()、或者我们退出导航后残留的选中态——
        ///   按空格推进剧情就会顺手把那个控件再点一次。
        ///
        ///   本模组的语义是：回车/空格只在导航模式里、且有选中项时才算提交，
        ///   其余一律归还给游戏。所以这条通路必须一直关着。
        ///
        /// 代价与安全性：
        ///   - 失去 uGUI 原生的方向键导航 —— 那正是本导航模式要替代的东西。
        ///   - 鼠标点按走 pointer 事件，不受影响。
        ///   - 输入框打字走 TMP_InputField 自己读 Input，不受影响；
        ///     游戏确认输入框也是自己读 Input.GetKeyDown(Return)。
        ///   - 游戏全代码没有一处 SetSelectedGameObject，本就不依赖选中态。
        ///   - 新场景的 EventSystem 默认是 true，所以每帧都要重申一次。
        ///
        /// 由 Plugin.Update 无条件调用（不受「菜单键盘导航」开关影响）：
        /// 这条通路一旦松开，鼠标点过的按钮就会在按空格推进剧情时被重复点击。
        /// </summary>
        internal static void KeepUnitySubmitOff()
        {
            try
            {
                EventSystem es = EventSystem.current;
                if (es == null) return;
                if (es.sendNavigationEvents) es.sendNavigationEvents = false;
            }
            catch { }
        }

        /// <summary>清掉我们自己设的选中态，去掉高亮、也不给 uGUI 留提交目标。</summary>
        private static void ReleaseSelection()
        {
            GameObject go = _selectedByUs;
            _selectedByUs = null;
            if (go == null) return;   // 已被销毁的也算 null，直接跳过
            try
            {
                EventSystem es = EventSystem.current;
                if (es != null && es.currentSelectedGameObject == go)
                    es.SetSelectedGameObject(null);
            }
            catch { }
        }

        /// <summary>把某个对象设成当前选中（用于让游戏自己的高亮态生效）。</summary>
        private static void SelectByUs(GameObject go)
        {
            if (go == null) return;
            try
            {
                EventSystem es = EventSystem.current;
                if (es == null) return;
                es.SetSelectedGameObject(go);
                _selectedByUs = go;
            }
            catch { }
        }

        private static bool SceneStable()
        {
            try
            {
                Scene sc = SceneManager.GetActiveScene();
                if (!sc.isLoaded) return false;
                return Time.realtimeSinceStartup - _sceneChangedAt >= SceneSettleSeconds;
            }
            catch { return false; }
        }

        // ================= 扫描与分组 =================

        private static void Scan()
        {
            int keepGroupSibling = (Groups.Count > 0 && _groupIndex >= 0 && _groupIndex < Groups.Count)
                ? Groups[_groupIndex].SiblingIndex : int.MinValue;
            Groups.Clear();
            _inputRoles.Clear();   // 实例 ID 会在对象销毁后被复用，每次重扫都重算
            _rescanRequestedFor = null;
            if (!SceneStable()) return;

            try
            {
                bool diag = Plugin.CfgDiag != null && Plugin.CfgDiag.Value;
                List<string> excluded = diag ? new List<string>() : null;
                int inactive = 0, noCanvas = 0, inactiveScene = 0, blockedCount = 0, notInteractable = 0;
                Scene activeScene = SceneManager.GetActiveScene();

                Selectable[] all = Resources.FindObjectsOfTypeAll<Selectable>();
                List<string> allDump = diag ? new List<string>() : null;
                for (int i = 0; i < all.Length; i++)
                {
                    Selectable s = all[i];
                    if (s == null) continue;                       // Unity 伪空：已销毁对象在此拦下

                    // 诊断：激活的优先记（那才是真正该导航的东西），未激活的只留几条当背景。
                    // 之前一律只记前 60 条，结果 333 个里前面全是未激活的存档槽，
                    // 真正有意义的激活控件一条都没进日志。
                    if (allDump != null)
                    {
                        bool act = s.isActiveAndEnabled;
                        if ((act && allDump.Count < 60) || (!act && allDump.Count < 80))
                        {
                            string scen = "?";
                            bool valid = false;
                            try { scen = s.gameObject.scene.name; valid = s.gameObject.scene.IsValid(); } catch { }
                            allDump.Add((act ? "[激活]  " : "[未激活]")
                                + " {" + scen + "}" + (valid ? "" : "(资源)") + " " + PathOf(s.transform));
                        }
                    }

                    if (!s.isActiveAndEnabled)
                    {
                        inactive++;
                        if (diag && inactiveScene < 20 && s.gameObject.scene.IsValid()
                            && s.gameObject.scene == activeScene)
                        {
                            excluded.Add("[未激活] " + PathOf(s.transform));
                            inactiveScene++;
                        }
                        continue;
                    }
                    if (!s.gameObject.scene.IsValid()) continue;   // 排除预制体资源
                    if (!(As<RectTransform>(s.transform) != null)) continue; // 只处理 UI

                    // ★ 开发者面板必须排除。Naninovel 自带一个 ScriptNavigatorUI，
                    //   里面列着「PlayScript: Title」这类**裸剧本跳转**条目。
                    //   它能被导航到、能被回车激活，一按就把游戏扔进裸剧本，
                    //   绕开正常流程状态机 —— 实测结果是黑屏 + 有音乐 + 无任何响应，
                    //   只能 Alt+F4。这不是游戏的问题，是我们不该把它扫进来。
                    if (Blocked(s.transform, diag ? excluded : null)) { blockedCount++; continue; }

                    // 游戏自己判定为「此刻不可用」的控件**不给人按**
                    // （实测：按下去会让游戏进到不该进的状态）。
                    //
                    // 但不再直接丢掉，而是先放进本组的「只读」篮子：可见性判定跑完之后，
                    // 如果这一组一个可用控件都不剩，就把它们作为**只读**导航项收进来 ——
                    // 只念，永远不激活（Activate 见到不可用一律只报「该项当前不可用」）。
                    // 本作的彩蛋设置界面（莉莉丝的设置界面）整屏都是这种控件，详见下面兜底处。
                    bool usable = s.interactable;
                    if (!usable) notInteractable++;

                    Group g = GroupOf(s);
                    if (g != null)
                    {
                        if (usable) g.Items.Add(s);
                        else g.ReadOnly.Add(s);
                    }
                    else if (diag && noCanvas < 10)
                    {
                        excluded.Add("[不在 Canvas 下] " + PathOf(s.transform));
                        noCanvas++;
                    }
                }

                // ★ 白名单界面里的「纯文本」也纳入导航。
                //
                // 回想（History）面板里每条台词就是一个纯 TMP_Text，没有对应的
                // Selectable —— 按原来的做法，读屏玩家打开回想之后什么都读不到。
                //
                // 把这些文字也做成导航项之后：方向键逐条翻、回车重念当前这条，
                // 等于把「重读上一句」和「回想朗读」两件事一次解决，
                // 而且是**游戏本来就有的交互**，不用我们再造一套。
                //
                // 两个过滤条件：
                //   · 跳过按钮/开关自带的标签 —— 那些由控件本身代表，重复念是噪声
                //   · 跳过被 CanvasGroup 藏起来的
                string wl = Plugin.CfgUiTextWhitelist != null ? Plugin.CfgUiTextWhitelist.Value : "";
                if (!string.IsNullOrEmpty(wl))
                {
                    string[] frags = wl.Split(',');
                    var texts = Resources.FindObjectsOfTypeAll<TMPro.TMP_Text>();
                    int addedText = 0;
                    for (int i = 0; i < texts.Length; i++)
                    {
                        var tx = texts[i];
                        if (tx == null || tx.gameObject == null) continue;
                        if (!tx.gameObject.activeInHierarchy) continue;

                        string body = tx.text;
                        if (string.IsNullOrEmpty(body) || body.Trim().Length == 0) continue;

                        // 控件自己的标签不算
                        try { if (tx.GetComponentInParent<Selectable>() != null) continue; } catch { }

                        string path = PathOf(tx.transform);
                        bool hit = false;
                        for (int k = 0; k < frags.Length; k++)
                        {
                            string f = frags[k].Trim();
                            if (f.Length > 0 && path.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) { hit = true; break; }
                        }
                        if (!hit) continue;
                        if (HiddenByCanvasGroup(tx.transform)) continue;

                        Group g = GroupOf(tx);
                        if (g != null) { g.Items.Add(tx); addedText++; }
                    }
                    if (diag && addedText > 0)
                        Plugin.L.LogInfo("[UiNav] 白名单文字纳入导航 " + addedText + " 条（" + wl + "）");
                }

                if (allDump != null && Plugin.L != null)
                {
                    var db = new StringBuilder();
                    db.Append("[UiNav] ==== 全部 Selectable（前 ").Append(allDump.Count).Append(" / ").Append(all.Length).Append("）====");
                    for (int i = 0; i < allDump.Count; i++) db.Append('\n').Append("  ").Append(allDump[i]);
                    Plugin.L.LogInfo(db.ToString());
                }

                // 只留下**真的在画面上**的控件。这一步必须在排序之前做：
                // 画面外/被挡住的控件不但念了没用，还会把编号撑大（「1 / 13」）。
                //
                // ★ 本作的实测教训：这套「可见性」判定是从上一部作品继承来的，
                //   在本作上可能**把全部控件都筛掉**，表现就是按 Tab 什么都不导航。
                //   所以加一道自愈兜底：严格控制后一组都没剩下、但确实存在激活控件时，
                //   自动退回「只要是激活的 Selectable 就收」的宽松口径，宁可多念也不要不念。
                int removedByVisibility = 0;
                bool strict = Plugin.CfgUiVisibleOnly == null || Plugin.CfgUiVisibleOnly.Value;

                if (strict) FilterByVisibility(true, diag ? excluded : null, ref removedByVisibility);

                // ★ 这里曾经有一个「自愈兜底」：严格控制后一个都不剩时，退回
                //   「只要是激活的 Selectable 就全收」。那是为了修「按 Tab 没反应」——
                //   而那个症状真正的病因是 IL2CPP 下 `as`/`is` 静默失败（见 As<T>），
                //   已经修好了。兜底留下只会把**隐藏面板全部重新变成可达**：
                //   实测在游戏内触发过一次，最终纳入从 1 个暴涨到 93 个、18 组，
                //   隐藏的存档界面和剧透面板全回来了 —— 正是玩家报告的那些问题。
                //
                //   原则：无障碍层只能让「已经可见、本来就能点」的东西变得可键盘操作，
                //   绝不能扩大可达范围。筛完没剩下东西，就诚实地告诉玩家没东西可导航。
                if (strict && removedByVisibility > 0)
                    DiagScan("严格可见性判定筛掉了 " + removedByVisibility + " 个控件（不做兜底，避免暴露隐藏面板）");

                // ★ 「整屏控件都不可用」的**只读**兜底（与上面删掉的「自愈兜底」不是一回事）。
                //
                // 本作的彩蛋设置界面（莉莉丝的设置界面）里，游戏把**全部**控件置成
                // interactable = false。实测日志（作者机器，2026-09-26）：
                //     不可用=14   被可见性筛掉=58   最终纳入=0
                // 于是这一屏在导航里变成「什么都没有」：读屏玩家既听不到屏幕上有什么，
                // 也不知道自己为什么卡住 —— 玩家报告原话是「设置界面不可用」。
                //
                // 兜底只做两件不越界的事：
                //   · 只收**可见**的控件（可见性判定照旧跑过一遍，隐藏面板一个都进不来）
                //   · 只收进导航，**永远不激活**（Activate 对不可用控件只报「该项当前不可用」）
                // 也就是把「看得见但点不动」变成「听得见但点不动」，
                // 没有让任何本来点不到的东西变得可点。可用控件还剩一个的组，照旧只收可用的。
                int readOnlyKept = 0;
                for (int i = 0; i < Groups.Count; i++)
                {
                    Group g = Groups[i];
                    if (g.Items.Count > 0) continue;
                    if (g.ReadOnly.Count == 0 && g.Blocked.Count == 0) continue;

                    g.Items.AddRange(g.ReadOnly);   // 看得见、游戏标成不可用
                    g.Items.AddRange(g.Blocked);    // 看得见、游戏标成可用但这一帧点不到（如「返回」）
                    readOnlyKept += g.Items.Count;
                }
                if (readOnlyKept > 0)
                    DiagScan("这一屏没有能按的控件，纳入 " + readOnlyKept
                        + " 个（只读的只念不点；看得见但点不到的照常可激活）");

                if (Plugin.L != null)
                {
                    int kept = 0;
                    for (int i = 0; i < Groups.Count; i++) kept += Groups[i].Items.Count;
                    Plugin.L.LogInfo(string.Format(
                        "[UiNav] Selectable 总数={0} 未激活={1} 不在Canvas下={2} 开发者面板排除={3} 不可用={4} 被可见性筛掉={5} 最终纳入={6}（其中只读 {7}）",
                        all.Length, inactive, noCanvas, blockedCount, notInteractable, removedByVisibility, kept, readOnlyKept));
                }

                // 组排序：Canvas 层级高的、兄弟序号靠后的（在更上层）排前面
                Groups.Sort((a, b) =>
                {
                    if (a.CanvasOrder != b.CanvasOrder) return b.CanvasOrder.CompareTo(a.CanvasOrder);
                    return b.SiblingIndex.CompareTo(a.SiblingIndex);
                });
                Groups.RemoveAll(g => g.Items.Count == 0);
                for (int i = 0; i < Groups.Count; i++) SortWithin(Groups[i]);

                _groupIndex = 0;
                if (keepGroupSibling != int.MinValue)
                {
                    int found = Groups.FindIndex(g => g.SiblingIndex == keepGroupSibling);
                    if (found >= 0) _groupIndex = found;
                }

                if (Plugin.L != null)
                {
                    var sb = new StringBuilder();
                    sb.Append("[UiNav] 扫描到 ").Append(Groups.Count).Append(" 组：");
                    for (int i = 0; i < Groups.Count && i < 6; i++)
                    {
                        sb.Append(Groups[i].Root != null ? Groups[i].Root.name : "?")
                          .Append('(').Append(Groups[i].Items.Count).Append(") ");
                    }
                    Plugin.L.LogInfo(sb.ToString());
                }

                DumpScan(excluded);
            }
            catch (Exception e)
            {
                Plugin.L.LogError("扫描界面控件失败: " + e.Message);
                Groups.Clear();
            }
        }

        private static void DiagScan(string msg)
        {
            try { if (Plugin.L != null) Plugin.L.LogInfo("[UiNav] " + msg); } catch { }
        }

        /// <summary>
        /// 该控件是否落在「排除的面板路径」里（默认就是 Naninovel 的开发者工具）。
        /// 用整条层级路径做**片段匹配**，所以面板改名换位置也能兜住。
        /// </summary>
        private static bool Blocked(Transform t, List<string> excluded)
        {
            try
            {
                var cfg = Plugin.CfgUiBlockedPaths;
                if (cfg == null || string.IsNullOrEmpty(cfg.Value)) return false;

                string path = PathOf(t);
                string[] frags = cfg.Value.Split(',');
                for (int i = 0; i < frags.Length; i++)
                {
                    string f = frags[i].Trim();
                    if (f.Length == 0) continue;
                    if (path.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (excluded != null && excluded.Count < 60)
                            excluded.Add("[已排除·开发者面板] " + path);
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>转到统一的可见性判定（见 UiVis.cs）。</summary>
        private static bool HiddenByCanvasGroup(Transform t)

        {
            return UiVis.Hidden(t);
        }

        /// <summary>
        /// **只按 alpha** 判「看不看得见」：沿父链只要有 CanvasGroup.alpha ≈ 0 就算看不见。
        ///
        /// 为什么单独要这条：UiVis.Hidden 把 `interactable = false` / `blocksRaycasts = false`
        /// 也算成"隐藏"，那对**朗读**是对的（别念隐藏面板里的字），
        /// 但对**导航**太粗 —— 本作的彩蛋设置界面就是 `alpha = 1` 正常显示、
        /// 而 `interactable = false` 点不动（实测：那一屏 不可用=14）。
        /// 两者混在一起判，结果是"看得见但点不动"被当成"看不见"直接丢掉了。
        /// </summary>
        private static bool AlphaHidden(Transform t)
        {
            return UiVis.HiddenByAlpha(t);
        }

        /// <summary>
        /// **只按矩形**判「在不在画面里」：控件矩形要和所在 Canvas 的矩形相交。
        /// 不看射线、不看 interactable —— 用于「看得见但点不动」这一类只读项。
        /// </summary>
        private static bool OnScreenOnly(Component s, Group g)
        {
            try
            {
                RectTransform rt = As<RectTransform>(s.transform);
                if (rt == null) return true;      // 判不出来就不排除
                if (!g.HasCanvasRect) return true;

                Rect r = WorldRect(rt);
                if (r.xMax < g.CanvasRect.xMin - 2f || r.xMin > g.CanvasRect.xMax + 2f) return false;
                if (r.yMax < g.CanvasRect.yMin - 2f || r.yMin > g.CanvasRect.yMax + 2f) return false;
                return true;
            }
            catch { return true; }
        }

        /// <summary>把「画面上点不到」的控件从各组里剔掉，并统计剔掉了几个。</summary>
        private static void FilterByVisibility(bool record, List<string> excluded, ref int removed)
        {
            for (int i = 0; i < Groups.Count; i++)
            {
                Group g = Groups[i];
                // 「能按的」照旧要过全部三档（能看到 + 能点到）；被筛掉但**看得见**的
                // 先存进 Blocked 备用（返回按钮就是这一类）
                FilterBlocked(g, record, excluded, ref removed);
                // 「只读」那一篮子放宽：只要**看得见**（alpha > 0）且在画面矩形里就留着 ——
                // 它们本来就点不动（interactable = false），要求"能点到"等于自相矛盾。
                // 这一篮子只有在整组没有可用控件时才会被启用，且永远不激活。
                FilterList(g, g.ReadOnly, record, excluded, ref removed, true);
            }
        }

        /// <summary>
        /// 「能按的」那一篮的过滤：过严格的可见性判定；被筛掉的那些如果**只是点不到**
        /// （alpha 还在、还在画面矩形里），留进 Blocked 备用 ——
        /// 整组一个能按的都不剩时它们会被当导航项收回来。
        /// 典型例子是彩蛋设置界面里的「返回」：`interactable = true`，
        /// 但父级 CanvasGroup 关了交互，严格判定会把它判成"画面外或点不到"，
        /// 于是玩家**被困在那一屏里出不来**（实测反馈）。
        /// </summary>
        private static void FilterBlocked(Group g, bool record, List<string> excluded, ref int removed)
        {
            var blocked = new List<Component>();
            int n = 0;
            g.Items.RemoveAll(s =>
            {
                bool ok;
                // 判定本身抛异常时按「可见」处理 —— 宁可多念一个，也不要整组消失
                try { ok = VisiblyClickable(s, g); } catch { ok = true; }
                if (ok) return false;

                n++;
                bool visible = false;
                try { visible = !AlphaHidden(s.transform) && OnScreenOnly(s, g); } catch { visible = false; }
                if (visible) blocked.Add(s);
                else if (record && excluded != null && excluded.Count < 60)
                    excluded.Add("[画面外或点不到] " + PathOf(s.transform));
                return true;
            });
            removed += n;

            if (blocked.Count > 0)
            {
                g.Blocked.AddRange(blocked);
                if (record && excluded != null && excluded.Count < 60)
                    excluded.Add("[看得见但点不到] " + blocked.Count + " 个（整屏没有能按的时才收回来）");
            }
        }

        /// <summary>
        /// 两份清单的过滤。readOnlyBucket = true 时用放宽口径
        /// （只看 alpha 与矩形；「点不点得动」对只读项没有意义）。
        /// </summary>
        private static void FilterList(Group g, List<Component> items, bool record, List<string> excluded,
            ref int removed, bool readOnlyBucket)
        {
            int n = 0;
            items.RemoveAll(s =>
            {
                bool ok;
                // 判定本身抛异常时按「可见」处理 —— 宁可多念一个，也不要整组消失
                try
                {
                    ok = readOnlyBucket
                        ? (!AlphaHidden(s.transform) && OnScreenOnly(s, g))
                        : VisiblyClickable(s, g);
                }
                catch { ok = true; }
                if (!ok)
                {
                    n++;
                    if (record && excluded != null && excluded.Count < 60)
                        excluded.Add((readOnlyBucket ? "[只读项也看不见] " : "[画面外或点不到] ") + PathOf(s.transform));
                }
                return !ok;
            });
            removed += n;
        }

        /// <summary>取该控件在 Canvas 下的顶层祖先作为分组依据。</summary>
        private static Group GroupOf(Component s)
        {
            try
            {
                Canvas c = s.GetComponentInParent<Canvas>();
                if (c == null) return null;
                Transform canvasT = c.transform;

                Transform top = s.transform;
                Transform t = s.transform;
                while (t != null && t != canvasT)
                {
                    top = t;
                    t = t.parent;
                }
                if (t == null) return null;   // 不在该 Canvas 下

                Group g = Groups.Find(x => x.Root == top);
                if (g == null)
                {
                    g = new Group
                    {
                        Root = top,
                        CanvasOrder = c.sortingOrder,
                        SiblingIndex = top.GetSiblingIndex(),
                        Camera = c.renderMode == RenderMode.ScreenSpaceOverlay ? null : c.worldCamera
                    };
                    RectTransform crt = As<RectTransform>(c.transform);
                    if (crt != null)
                    {
                        g.CanvasRect = WorldRect(crt);
                        g.HasCanvasRect = true;
                    }
                    Groups.Add(g);
                }
                return g;
            }
            catch { return null; }
        }

        /// <summary>
        /// 组内排序：**按屏幕位置**（先上后下，同一行先左后右），
        /// 位置重合时才退回渲染层级（兄弟序号路径）决定先后。
        ///
        /// === 为什么改成位置优先 ===
        /// 原来以兄弟序号路径为主。但路径表达的是「谁后渲染、谁在上层」，
        /// 跟画面上看到的上下左右没有必然关系：实测有界面出现
        /// 「上面的控件是 2/x、下面的反而是 1/x」。
        /// 读屏用户是靠「第几项」建立空间印象的（部分视力用户还会
        /// 对着屏幕找），顺序必须和画面一致，否则每项都要重新试。
        ///
        /// 行号用「向上量化到 4 像素」的格子算，而不是直接比浮点 y：
        /// 比较函数必须是**可传递**的全序。若用「差值小于阈值就算同一行」
        /// 这种写法，可能出现 a≈b、b≈c 但 a 与 c 不同行的情况，
        /// List.Sort 会抛「比较函数不一致」，而这里被 try 兜住后
        /// 会把整组清空、界面导航直接退出。量化不存在这个问题。
        /// </summary>
        private static void SortWithin(Group g)
        {
            // 开关见配置「按屏幕位置排序控件」：关掉就退回旧的渲染层级排序，
            // 方便对比「顺序错乱 / 控件定位不到」到底是不是这个改动引起的。
            bool byPosition = Plugin.CfgUiSortByPosition == null || Plugin.CfgUiSortByPosition.Value;

            var keys = new List<ItemKey>(g.Items.Count);
            for (int i = 0; i < g.Items.Count; i++)
            {
                Component s = g.Items[i];
                var k = new ItemKey { S = s, Row = int.MinValue, Left = float.MaxValue, Path = null };
                try
                {
                    RectTransform rt = s != null ? As<RectTransform>(s.transform) : null;
                    if (byPosition && rt != null)
                    {
                        float top, left;
                        if (ScreenTopLeft(rt, g.Camera, out top, out left))
                        {
                            k.Left = left;
                            k.Row = Mathf.RoundToInt(top / 4f);   // 屏幕像素，4 像素一档
                        }
                    }
                    if (s != null) k.Path = PathIndices(s.transform, g.Root).ToArray();
                }
                catch { }
                keys.Add(k);
            }

            if (byPosition) keys.Sort(CompareKey);
            else keys.Sort(ComparePathOnly);

            g.Items.Clear();
            for (int i = 0; i < keys.Count; i++) g.Items.Add(keys[i].S);
        }

        private struct ItemKey
        {
            public Component S;
            public int Row;        // 屏幕上下：值越大越靠上
            public float Left;     // 屏幕左右：值越小越靠左
            public int[] Path;     // 渲染层级，仅用于位置完全重合时
        }

        /// <summary>
        /// ⚠️ 必须是 Il2CppStructArray，**不能**是托管 Vector3[]。
        ///
        /// 这是本项目第三个「IL2CPP 静默失败」：
        /// 托管数组传给原生方法时会被**拷贝**过去，原生写入的结果不会回流。
        /// 于是 GetWorldCorners 之后 _corners 仍然是全 0 —— 而它不报错。
        ///
        /// 后果非常隐蔽：所有控件的「屏幕位置」都是 (0,0)，
        ///   · 排序全部并列 -> 退化成按层级路径排 -> **上下键顺序和画面对不上**
        ///   · 可见性判断里的「屏幕矩形是否相交」恒真，等于这道检查形同虚设
        /// 一直没人发现，是因为射线那道检查还在正常工作、把大部分问题兜住了。
        /// </summary>
        private static readonly Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3> _corners =
            new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);

        private static int CompareKey(ItemKey a, ItemKey b)
        {
            // 必须是**严格全序**：List.Sort 用的是内省排序，比较函数只要不满足
            // 传递性，排出来的结果就是任意的（而且不报错）。原版这里是
            //     if (Mathf.Abs(a.Left - b.Left) > 0.01f) return a.Left.CompareTo(b.Left);
            // 拿「近似相等」当分支条件，正是典型的非传递比较：
            // a≈b、b≈c 但 a 与 c 差得远时，三者顺序可以任意。
            // 现在改成严格的字典序：行 -> 列 -> 渲染层级。
            if (a.Row != b.Row) return b.Row.CompareTo(a.Row);   // 上 → 下
            int c = a.Left.CompareTo(b.Left);                    // 左 → 右
            if (c != 0) return c;
            return CompareIndexPath(a.Path, b.Path);             // 完全重合看渲染层级
        }

        private static int ComparePathOnly(ItemKey a, ItemKey b)
        {
            return CompareIndexPath(a.Path, b.Path);
        }

        private static int CompareIndexPath(int[] pa, int[] pb)
        {
            if (pa == null || pb == null) return 0;
            int n = Mathf.Min(pa.Length, pb.Length);
            for (int i = 0; i < n; i++)
            {
                if (pa[i] != pb[i]) return pb[i].CompareTo(pa[i]);   // 后渲染的在上层
            }
            return pb.Length.CompareTo(pa.Length);   // 更深的（更靠内层）在后
        }

        private static List<int> PathIndices(Transform t, Transform root)
        {
            var list = new List<int>();
            Transform cur = t;
            while (cur != null && cur != root)
            {
                list.Add(cur.GetSiblingIndex());
                cur = cur.parent;
            }
            list.Reverse();
            return list;
        }

        // ================= 可见性判断 =================

        /// <summary>RectTransform 的世界坐标外接矩形。</summary>
        private static Rect WorldRect(RectTransform rt)
        {
            rt.GetWorldCorners(_corners);   // 0=左下 1=左上 2=右上 3=右下
            float minX = Mathf.Min(Mathf.Min(_corners[0].x, _corners[1].x), Mathf.Min(_corners[2].x, _corners[3].x));
            float maxX = Mathf.Max(Mathf.Max(_corners[0].x, _corners[1].x), Mathf.Max(_corners[2].x, _corners[3].x));
            float minY = Mathf.Min(Mathf.Min(_corners[0].y, _corners[1].y), Mathf.Min(_corners[2].y, _corners[3].y));
            float maxY = Mathf.Max(Mathf.Max(_corners[0].y, _corners[1].y), Mathf.Max(_corners[2].y, _corners[3].y));
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        /// <summary>
        /// 控件是不是**真的在画面上**：矩形要和 Canvas 相交，而且从它中心发出的一次
        /// UI 射线要能打到它（或它的子物体）身上。
        ///
        /// === 为什么必须加这道判断 ===
        /// 标题场景里 `Canvas/Panel/Phone/...` 那整套按钮是 active 的，但手机面板
        /// 停在画面外（anchoredPosition.x = 1260，而 START/EXIT 在 -2713），
        /// 屏幕上一个像素都看不到。只判断 isActiveAndEnabled 会把它们全纳入导航 ——
        /// 于是画面上明明是 START/EXIT，方向键却在走「读档 / 存档」，
        /// 按回车还真的弹出读档确认框，因为 **Unity 的按钮根本不关心自己有没有被画出来**。
        ///
        /// 判不出来时一律「不排除」：少一个控件是功能缺失，多一个控件只是噪声。
        /// </summary>
        private static bool VisiblyClickable(Component s, Group g)
        {
            try
            {
                RectTransform rt = As<RectTransform>(s.transform);
                if (rt == null) return true;

                // 0) ★ CanvasGroup 淡出 / 禁用的面板一律不算可见。
                //
                //    Naninovel 的面板是靠 CanvasGroup 做显隐的：面板藏起来时
                //    GameObject 仍然是 active，raycast 也照样命中（blocksRaycasts 还是 true），
                //    光靠「射线打得到自己」会把**整块隐藏面板**判成可见 ——
                //    实测这就是两个恶性问题的共同入口：
                //      · 按 PageUp/PageDown 能翻进玩家此刻不该看到的剧透面板；
                //      · 能点中当前状态下根本不该能点的控件，把游戏搞坏。
                if (HiddenByCanvasGroup(s.transform)) return false;

                // 和 Canvas 矩形完全不相交 = 画面外
                if (g.HasCanvasRect)
                {
                    Rect r = WorldRect(rt);
                    if (r.xMax < g.CanvasRect.xMin - 2f || r.xMin > g.CanvasRect.xMax + 2f) return false;
                    if (r.yMax < g.CanvasRect.yMin - 2f || r.yMin > g.CanvasRect.yMax + 2f) return false;
                }

                // 2) 从中心打一条 UI 射线，看能不能打到自己
                EventSystem es = EventSystem.current;
                if (es == null) return true;

                Vector3 world = rt.TransformPoint(rt.rect.center);
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(g.Camera, world);
                var ped = new PointerEventData(es) { position = screen };
                // IL2CPP 下 RaycastAll 只认 Il2Cpp 的 List，不能用 System.Collections.Generic.List
                var hits = new Il2CppSystem.Collections.Generic.List<RaycastResult>();
                es.RaycastAll(ped, hits);
                if (hits.Count == 0) return true;      // 射线本身打不到任何东西 → 判不出来，不排除

                for (int i = 0; i < hits.Count; i++)
                {
                    GameObject hit = hits[i].gameObject;
                    if (hit == null) continue;
                    // 只认「打到自己或自己的子物体」。**不能**认祖先：
                    // 整屏背景通常就是所有控件的共同祖先，认了它等于这道判断失效。
                    if (hit == s.gameObject || hit.transform.IsChildOf(s.transform)) return true;
                }
                return false;
            }
            catch { return true; }
        }

        // ================= 诊断（配置「界面诊断日志」打开时才输出）=================

        /// <summary>控件在层级里的路径，形如 Canvas/Panel/Confirm/Yes。</summary>
        internal static string PathOf(Transform t)
        {
            try
            {
                var stack = new List<string>();
                Transform cur = t;
                int guard = 0;
                while (cur != null && guard++ < 12)
                {
                    stack.Add(cur.gameObject.name);
                    cur = cur.parent;
                }
                var sb = new StringBuilder();
                for (int i = stack.Count - 1; i >= 0; i--)
                {
                    if (sb.Length > 0) sb.Append('/');
                    sb.Append(stack[i]);
                }
                return sb.ToString();
            }
            catch { return "?"; }
        }

        private static string RowLeftOf(Component s)
        {
            try
            {
                RectTransform rt = s != null ? As<RectTransform>(s.transform) : null;
                if (rt == null) return "位置=?";
                float top, left;
                if (!ScreenTopLeft(rt, s.GetComponentInParent<Canvas>() != null
                        ? CanvasCamera(s) : null, out top, out left))
                    return "位置=?";
                return "行=" + Mathf.RoundToInt(top / 4f) + " 顶=" + Mathf.RoundToInt(top)
                     + " 左=" + Mathf.RoundToInt(left);
            }
            catch { return "位置=?"; }
        }

        private static Camera CanvasCamera(Component c)
        {
            try
            {
                var cv = c.GetComponentInParent<Canvas>();
                if (cv == null) return null;
                return cv.renderMode == RenderMode.ScreenSpaceOverlay ? null : cv.worldCamera;
            }
            catch { return null; }
        }

        /// <summary>
        /// 取控件在**屏幕像素**坐标下的上边与左边。
        ///
        /// ⚠️ 必须换算，不能直接用 GetWorldCorners 的原始值。
        /// 世界坐标的尺度取决于 Canvas 的 renderMode 与缩放：本作某些画布
        /// 的世界单位极小（实测顶边 ≈ 4，而屏幕上是 1000 上下），
        /// 而排序是按 `顶 / 4` 分档的 —— 拿世界坐标分档会把所有条目
        /// 压进同一档，然后退化成按左右排，**上下顺序就全乱了**。
        /// 这正是玩家报的「按下光标反而跳到上面」剩下的那一半原因。
        /// </summary>
        private static bool ScreenTopLeft(RectTransform rt, Camera cam, out float top, out float left)
        {
            top = 0f; left = 0f;
            try
            {
                rt.GetWorldCorners(_corners);   // 0=左下 1=左上 2=右上 3=右下
                Vector2 tl = RectTransformUtility.WorldToScreenPoint(cam, _corners[1]);
                Vector2 tr = RectTransformUtility.WorldToScreenPoint(cam, _corners[2]);
                Vector2 bl = RectTransformUtility.WorldToScreenPoint(cam, _corners[0]);
                top = Mathf.Max(tl.y, tr.y);
                left = Mathf.Min(bl.x, tl.x);
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// 把这次扫描的结果写进日志：每一组的每一项念什么、在屏幕哪儿、层级路径是什么，
        /// 以及「场景里有、但没被纳入导航」的控件。
        /// 我这边没法启动游戏，只能靠这份日志定位「某个控件找不到」「只念类型不念文字」。
        /// </summary>
        private static void DumpScan(List<string> excluded)
        {
            try
            {
                if (Plugin.CfgDiag == null || !Plugin.CfgDiag.Value) return;
                if (Plugin.L == null) return;

                var sb = new StringBuilder();
                sb.Append("[UiNav] ===== 诊断：共 ").Append(Groups.Count).Append(" 组 =====");
                for (int gi = 0; gi < Groups.Count; gi++)
                {
                    Group g = Groups[gi];
                    sb.Append("\n[组 ").Append(gi + 1).Append(gi == _groupIndex ? " ★当前" : "")
                      .Append("] ").Append(g.Root != null ? PathOf(g.Root) : "?")
                      .Append("  项数=").Append(g.Items.Count);
                }
                if (Groups.Count > 0 && _groupIndex >= 0 && _groupIndex < Groups.Count)
                {
                    Group g = Groups[_groupIndex];
                    for (int i = 0; i < g.Items.Count && i < 14; i++)
                    {
                        Component s = g.Items[i];
                        string desc = "?";
                        try { desc = Describe(s); } catch (Exception e) { desc = "<Describe 抛异常: " + e.Message + ">"; }
                        sb.Append("\n  #").Append(i + 1).Append(' ').Append(desc)
                          .Append("   [").Append(s != null ? RowLeftOf(s) : "null").Append(']')
                          .Append("  ").Append(s != null ? PathOf(s.transform) : "null");
                    }
                }
                if (excluded != null && excluded.Count > 0)
                {
                    sb.Append("\n[有控件但没纳入导航 ").Append(excluded.Count).Append(" 条]");
                    for (int i = 0; i < excluded.Count; i++) sb.Append("\n  ").Append(excluded[i]);
                }
                sb.Append("\n[UiNav] ==== 诊断结束 ====");
                Plugin.L.LogInfo(sb.ToString());
            }
            catch (Exception e)
            {
                try { Plugin.L.LogError("诊断输出失败: " + e.Message); } catch { }
            }
        }

        // ================= 描述 =================

        /// <summary>
        /// 拼接该控件及其子物体里的文本。
        /// 只取第一个会踩坑：SaveSlot 的 saveNameText / saveTimeText /
        /// chapterText 是三个字段，5 个空槽的第一个文本都是「无存档」，
        /// 读出来完全一样，用户无法区分。
        /// </summary>
        /// <summary>把一段文本压成可朗读的一行（去换行、去空白）。</summary>
        private static string Norm(string txt)
        {
            if (string.IsNullOrEmpty(txt)) return "";
            return txt.Replace("\n", " ").Replace("\r", " ").Replace("\t", " ").Trim();
        }

        /// <summary>控件自己子树里的文本（最多 3 段）。没有则返回空串。</summary>
        private static string OwnTextOf(Component s)
        {
            var parts = new List<string>();
            try
            {
                TextMeshProUGUI[] all = s.GetComponentsInChildren<TextMeshProUGUI>(true);
                for (int i = 0; i < all.Length && parts.Count < 3; i++)
                {
                    if (all[i] == null) continue;
                    string txt = Norm(all[i].text);
                    if (txt.Length == 0) continue;
                    bool dup = false;
                    for (int j = 0; j < parts.Count; j++)
                        if (parts[j] == txt) { dup = true; break; }
                    if (!dup) parts.Add(txt);
                }
            }
            catch { }
            return parts.Count == 0 ? "" : string.Join("，", parts.ToArray());
        }

        /// <summary>
        /// 「美术字标签」的对象名 → 中文。
        ///
        /// 本作设置界面的行名**大多是 TMP 文字**（MusicVolumeLabel / VoiceVolumeLabel /
        /// MessageSpeedLabel …），运行期有内容，优先走上面那条「同行兄弟文本」的路 ——
        /// 修好 FirstTextOutside 之后三条音量滑条都能念出行名了。
        /// 这张表是**兜底**：TMP 里取不到字时才退回对象名。
        ///
        /// 每一条都要有依据，不要凭感觉往里加：
        ///   · 行容器名（BgmVolumePanel / MessageSpeedPanel …）取自实测控件树，
        ///     来源是 BepInEx 日志里的层级路径 Naninovel&lt;Runtime&gt;/ModalUI/SettingsUI/…
        ///   · 中文名取自游戏自己的本地化表（SettingsMenu.Language / 音量 / 文本速度 …）
        ///   · 纯图片按钮几条取自 alert.confirm = 确定 / alert.cancel = 取消
        ///
        /// （上一版这里放的是**上一个作品** TransparentHer 的行名，
        ///   General / ScreenPixelOption / CharacterVolumePanel 这些对象在本作里根本不存在，
        ///   留着只会让人以为它们还在生效。已按本作实查的对象名重写。）
        ///
        /// 别名只在控件**自己子树里没有文字**时才会被用到（见 TextOf），
        /// 所以不会盖掉正常按钮的朗读。
        /// </summary>
        private static readonly Dictionary<string, string> NameAlias =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // ---- 设置界面的行容器（TMP 取不到时的兜底）----
            { "ScreenModePanel",     "画面模式" },
            { "MessageSpeedPanel",   "文本显示速度" },
            { "BgmVolumePanel",      "背景音乐音量" },
            { "SfxVolumePanel",      "音效音量" },
            { "VoiceVolumePanel",    "角色语音音量" },
            { "LocaleLanguagePanel", "界面语言" },
            { "LocalePanel",         "界面语言" },
            { "VoiceLocalePanel",    "角色语音语言" },

            // ---- 纯图片按钮 ----
            { "Yes",     "确定" },
            { "No",      "取消" },
            { "Confirm", "确定" },
            { "Cancel",  "取消" },
            { "Exit",    "退出" },
            { "Skip",    "跳过" },
        };

        /// <summary>
        /// 没有信息量的样板对象名。上溯找「有意义的名字」时要跳过它们，
        /// 否则永远停在 Slider / Toggle / ControlButton 上。
        /// </summary>
        private static bool IsBoilerplateName(string n)
        {
            if (string.IsNullOrEmpty(n)) return true;
            switch (n.ToLowerInvariant())
            {
                case "button": case "controlbutton": case "toggle": case "slider":
                case "text": case "image": case "textimage": case "rawimage":
                case "panel": case "option": case "options": case "labels":
                case "background": case "checkmark": case "line": case "staticpic":
                case "content": case "item": case "root": case "group":
                case "fill": case "handle": case "area":
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 从 parent 的子树里取第一段**不属于 exclude 子树**的文本。
        ///
        /// 两个排除条件都很重要：
        ///   - 排除自己那一支，否则可能读到自己内部的字；
        ///   - 排除落在**别的控件**（按钮/开关/滑条）里的文本，
        ///     那是那个控件的标签，不是这一行的标签。
        /// 另外只接受在层级中处于激活状态的文本，免得读到隐藏面板的字。
        ///
        /// 这里同时认 TMP 和旧版 UnityEngine.UI.Text：设置面板的 Text 节点
        /// 从场景里读不到静态文字（运行期才由本地化表填进去），无法确认是哪一种，
        /// 两种都认最省事，也不会有副作用。
        /// </summary>
        /// <summary>
        /// parent 子树里除了 exclude（这个控件自己那一支）之外，还有没有别的 Selectable。
        ///
        /// 用来区分「一行」和「一个面板」：
        ///   · 一行（设置面板的 BGMSlider 之类）里只有这一个控件 + 一段标签文字
        ///   · 面板（手机面板的 Buttons/Image）里塞着一堆控件
        /// 面板里的文字是标题，不能当成本控件的标签 —— 手机面板顶上那行「通讯」
        /// 原来就是这样被当成了里面**每一个**按钮的标签，整屏按钮全念「通讯」。
        /// </summary>
        private static bool HasOtherSelectable(Transform parent, Transform exclude)
        {
            try
            {
                Selectable[] all = parent.GetComponentsInChildren<Selectable>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    Selectable s = all[i];
                    if (s == null) continue;
                    Transform t = s.transform;
                    if (t == exclude || t.IsChildOf(exclude)) continue;
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static string FirstTextOutside(Transform parent, Transform exclude, bool allowSiblingControls)
        {
            try
            {
                // 这个容器里还有别的控件 → 它**通常**是「面板」，里面的文字是标题，
                // 不是某一行的标签（手机面板顶上那行「通讯」曾被当成里面每个按钮的标签）。
                //
                // ★ 但**控件自己那一层**必须放行。本作设置界面的一行长这样：
                //
                //     BgmVolumePanel/
                //       MusicVolumeLabel   ← 行名（TMP，控件的**兄弟**）
                //       SettingsSlider     ← 控件，子树里只有 Background / Fill Area / Handle
                //       BtnMute            ← 同一行里的第二个控件（静音）
                //
                //   严格判定会因为「这一层还有别的控件」而把整行作废，滑条于是退回对象名 ——
                //   实测（BepInEx 日志）三条音量滑条全念成「SettingsSlider，滑条，100%」，
                //   只有没有静音按钮的「文本显示速度」念对了。
                //
                //   放行是安全的：下面挑候选文本时，落在**别的 Selectable 子树里**的字
                //   一律排除，所以静音按钮自己的标签（「静音」）进不来。
                if (!allowSiblingControls && HasOtherSelectable(parent, exclude)) return "";

                var cands = new List<Component>();
                try { cands.AddRange(parent.GetComponentsInChildren<TextMeshProUGUI>(true)); }
                catch { }
                try { cands.AddRange(parent.GetComponentsInChildren<Text>(true)); }
                catch { }

                for (int i = 0; i < cands.Count; i++)
                {
                    Component c = cands[i];
                    if (c == null) continue;
                    if (!c.gameObject.activeInHierarchy) continue;

                    Transform tt = c.transform;
                    if (tt == exclude || tt.IsChildOf(exclude)) continue;

                    // 从这段文字往上走，只要在本行范围内遇到别的 Selectable，就说明
                    // 它属于那个控件，不是行标签。
                    bool other = false;
                    Transform cur = tt;
                    while (cur != null && cur != parent)
                    {
                        if (cur.GetComponent<Selectable>() != null) { other = true; break; }
                        cur = cur.parent;
                    }
                    if (other) continue;

                    string txt = TextOn(c);
                    if (txt.Length > 0) return txt;
                }
            }
            catch { }
            return "";
        }

        /// <summary>取组件上的文本，TMP 与旧版 Text 都认。</summary>
        private static string TextOn(Component c)
        {
            TextMeshProUGUI tmp = As<TextMeshProUGUI>(c);
            if (tmp != null) return Norm(tmp.text);
            Text legacy = As<Text>(c);
            if (legacy != null) return Norm(legacy.text);
            return "";
        }

        /// <summary>同一个「行」里的标签文本：从自己往上找，最多两层。</summary>
        private static string RowTextOf(Component s)
        {
            try
            {
                Transform t = s.transform;
                for (int up = 0; up < 2 && t != null; up++)
                {
                    Transform p = t.parent;
                    if (p == null) break;
                    // 第 0 层 = 控件自己所在的那一行：允许这一层里还有别的控件
                    string found = FirstTextOutside(p, t, up == 0);
                    if (found.Length > 0) return found;
                    t = p;
                }
            }
            catch { }
            return "";
        }

        /// <summary>两个说法是不是同一个东西（一个把另一个包含进去了）。</summary>
        private static bool RowNameCovers(string row, string near)
        {
            try
            {
                if (string.IsNullOrEmpty(row) || string.IsNullOrEmpty(near)) return false;
                return row.IndexOf(near, StringComparison.Ordinal) >= 0
                    || near.IndexOf(row, StringComparison.Ordinal) >= 0;
            }
            catch { return false; }
        }

        /// <summary>别名命中的是不是控件自己（而不是某一级祖先行名）。</summary>
        private static bool AliasOnSelf(Component s)
        {
            try
            {
                string n = s.gameObject.name;
                return n != null && NameAlias.ContainsKey(n);
            }
            catch { return false; }
        }

        /// <summary>从自己往上（最多 4 层）找第一个命中中文别名表的祖先名。</summary>
        private static string AliasAncestorOf(Component s)
        {
            try
            {
                Transform t = s.transform;
                for (int up = 0; up < 4 && t != null; up++)
                {
                    string n = t.gameObject.name;
                    string alias;
                    if (n != null && NameAlias.TryGetValue(n, out alias)) return alias;
                    t = t.parent;
                }
            }
            catch { }
            return "";
        }

        /// <summary>
        /// 控件的可读标签。
        ///
        /// === 为什么要分三层找（设置面板实测结构）===
        ///
        ///   TextSpeed        [行]
        ///     Text            ← 中文「文本显示速度」，是控件的**兄弟**，不是子节点
        ///     Slider          ← 控件，子树里只有 Background / Fill Area / Handle
        ///   ScreenPixel1080  [单选项]
        ///     Text            ← 「1280×720」
        ///     Toggle          ← 控件
        ///
        /// 只查控件自己的子树，slider 和 toggle 都会一个字都找不到，
        /// 于是退回对象名，读出「Slider」「Toggle」——只有类型，没有用途。
        ///
        ///   1) 自己子树有文本 → 直接用（存档槽、按钮等绝大多数情况走这条，行为不变）
        ///   2) 同一行的兄弟文本 → 用，并在前面补上所属行名（窗口分辨率、画面模式…）
        ///   3) 都没有 → 从自己往上找第一个有意思的名字，跳过样板名
        /// </summary>
        private static string TextOf(Component s)
        {
            string own = OwnTextOf(s);
            if (own.Length > 0) return own;

            string row = AliasAncestorOf(s);
            string near = RowTextOf(s);
            if (near.Length > 0)
            {
                if (row.Length > 0 && row != near)
                {
                    // 别名命中控件**自己**时（确认框的 Yes/No 这类纯图片按钮），
                    // 面板上的问题读在前、按钮名读在后：
                    //   「确定要使用这个姓名吗，确定，按钮」
                    // 别名来自祖先行名时保持原顺序（「窗口分辨率，1280×720」）。
                    if (AliasOnSelf(s)) return near + "，" + row;

                    // 行名和同行文字说的**是同一件事**时（「背景音乐音量」/「背景音乐」）
                    // 只念一次，念更完整的那一个。现在的行名兜底表是按行容器给的，
                    // 而 TMP 里往往也有同义的说法，不去重就会读成
                    // 「背景音乐音量，背景音乐」。
                    if (RowNameCovers(row, near))
                        return row.Length >= near.Length ? row : near;

                    return row + "，" + near;
                }
                return near;
            }

            if (row.Length > 0) return row;

            return AncestorNameOf(s);
        }

        /// <summary>兜底：从自己往上找第一个不是样板名的对象名。</summary>
        private static string AncestorNameOf(Component s)
        {
            try
            {
                Transform t = s.transform;
                for (int up = 0; up < 4 && t != null; up++)
                {
                    string n = t.gameObject.name;
                    if (!IsBoilerplateName(n)) return n;
                    t = t.parent;
                }
            }
            catch { }
            return s.gameObject.name;
        }

        // ================= 姓名输入框 =================
        //
        // level2（开局输入姓名）的两个输入框在场景里**没有任何标签文字**：
        // 行名画在图片上，TMP 里读不到；对象名一个叫 FirstText（InputField）、
        // 另一个叫 Third —— 后者尤其看不出是「名」。
        //
        // 唯一可靠的依据是游戏自己的 NameInputManagerTMP（反编译）：
        //     public TMP_InputField surnameField1;   ← 姓
        //     public TMP_InputField nameField1;      ← 名
        // Unity 按声明顺序序列化，解析 level2 的组件字节确认这两个字段
        // 分别指向场景里 FirstText（InputField）和 Third（恰好就是仅有的
        // 两个激活输入框，Second / Fourth 未激活）。
        //
        // 这里用反射读，读不到就一路回退（占位提示 → 同行标签 → 对象名），
        // 不让插件因为游戏改版而失效。

        private static readonly Dictionary<int, string> _inputRoles = new Dictionary<int, string>();

        /// <summary>
        /// 本作没有「开局输入姓名」那种无标签输入框，所以这里不做特判。
        /// 原实现依赖上一部作品自己的 NameInputManagerTMP 反射，属于游戏专属代码，
        /// 移植时整段删掉 —— 回退链（占位提示 → 同行标签 → 对象名）本来就能兜住。
        /// </summary>
        private static string InputFieldRoleName(TMP_InputField inf)
        {
            return "";
        }

        /// <summary>输入框的占位提示文字（TMP 与旧版 Text 都认）。</summary>
        private static string PlaceholderText(TMP_InputField inf)
        {
            try
            {
                Graphic g = inf.placeholder;
                return g != null ? TextOn(g) : "";
            }
            catch { return ""; }
        }

        /// <summary>
        /// 输入框的标签。**不能**走 TextOf：它的第一层 OwnTextOf 读的是
        /// 输入框自己子树里的 TMP，那是「已输入的内容」和占位提示，
        /// 念出来会变成「李，输入框，当前内容 李」这种重复噪声。
        /// </summary>
        private static string InputFieldLabel(TMP_InputField inf)
        {
            string role = InputFieldRoleName(inf);
            if (role.Length > 0) return role;

            string ph = PlaceholderText(inf);
            if (ph.Length > 0) return ph;

            string row = AliasAncestorOf(inf);
            string near = RowTextOf(inf);
            if (near.Length > 0) return (row.Length > 0 && row != near) ? row + "，" + near : near;
            if (row.Length > 0) return row;

            return AncestorNameOf(inf);
        }

        /// <summary>滑条当前值的说法。0-1 范围的条按百分比念，否则念 N / M。</summary>
        private static string SliderValueText(Slider sl)
        {
            if (sl.minValue >= -0.001f && sl.maxValue <= 1.001f)
                return Mathf.RoundToInt(Mathf.Clamp01(sl.value) * 100f) + "%";
            return Mathf.RoundToInt(sl.value) + " / " + Mathf.RoundToInt(sl.maxValue);
        }

        private static string Describe(Component s)
        {
            var sb = new StringBuilder();

            Toggle t = As<Toggle>(s);
            Slider sl = As<Slider>(s);
            TMP_InputField inf = As<TMP_InputField>(s);

            sb.Append(inf != null ? InputFieldLabel(inf) : TextOf(s));

            if (inf != null)
            {
                sb.Append("，输入框");
                string cur = Norm(inf.text);
                if (cur.Length > 0) sb.Append("，当前内容 ").Append(cur);
                else sb.Append("，当前为空");
                if (inf.characterLimit > 0 && inf.characterLimit <= 8)
                    sb.Append("，最多 ").Append(inf.characterLimit).Append(" 个字");
            }
            else if (t != null) sb.Append("，开关，").Append(t.isOn ? "开" : "关");
            else if (sl != null) sb.Append("，滑条，").Append(SliderValueText(sl));
            else if (As<Button>(s) != null) sb.Append("，按钮");
            else sb.Append("，文字");

            var _sel = As<Selectable>(s); if (_sel != null && !_sel.interactable) sb.Append("，不可用");
            return sb.ToString();
        }

        /// <summary>该组里有多少个「点得动」的控件（只读兜底进来的不算）。</summary>
        private static int UsableCount(Group g)
        {
            int n = 0;
            for (int i = 0; i < g.Items.Count; i++)
            {
                try
                {
                    var s = As<Selectable>(g.Items[i]);
                    if (s != null && s.interactable) n++;
                }
                catch { }
            }
            return n;
        }

        private static void AnnounceGroup(bool withCount)
        {
            if (Groups.Count == 0 || Items.Count == 0) return;
            string prefix = withCount
                ? ("导航模式，第 " + (_groupIndex + 1) + " 组，共 " + Items.Count + " 项。")
                : "";

            // 整组都是「只读」时先说清楚，免得玩家以为补丁坏了：
            // 这是游戏自己把这些控件标成不可用的（本作的彩蛋设置界面就是整屏如此）。
            try
            {
                if (UsableCount(Groups[_groupIndex]) == 0)
                    prefix += "这一屏的控件当前都不可用，只能听，不能选。";
            }
            catch { }

            Announce(prefix);
        }

        private static void Announce(string prefix)
        {
            if (!_active || Items.Count == 0) return;
            _index = Mathf.Clamp(_index, 0, Items.Count - 1);
            Component s = Items[_index];
            if (s == null) { ExitInternal(false); return; }

            _announcedItem = s;
            SelectByUs(s.gameObject);

            // 光标移到别处 = 放弃刚才那次退出确认
            ClearQuitConfirm();

            Speech.Speak(prefix + Describe(s) + "。" + (_index + 1) + " / " + Items.Count, true);
        }

        private static void SwitchGroup(int dir)
        {
            if (Groups.Count <= 1)
            {
                Speech.Speak("只有一个面板组。", true);
                return;
            }
            _groupIndex = (_groupIndex + dir + Groups.Count) % Groups.Count;
            _index = 0;
            AnnounceGroup(true);
        }

        // ================= 操作 =================

        /// <summary>当前选中项；导航模式未生效或该项已失效时返回 null。</summary>
        private static Component CurrentItem()
        {
            if (!_active) return null;
            if (_groupIndex < 0 || _groupIndex >= Groups.Count) return null;
            if (Items.Count == 0) return null;
            int i = Mathf.Clamp(_index, 0, Items.Count - 1);
            Component s = Items[i];
            return s == null ? null : s;   // Unity 伪空：已销毁对象在此拦下
        }

        /// <summary>
        /// 本帧游戏自身那次「推进剧情」是否该被拦掉。
        ///
        /// DialogueSceneManager / Ending2DialogueManager 的 Update 都是在
        /// Input.GetKeyDown(空格/回车) 成立后紧接着调 DialogueButtonClicked()。
        /// 只要那次按键已经归我们（或即将归我们），这次调用就该拦掉，
        /// 剧情才只动一次。
        ///
        /// 两种判据都要有，因为两个 Update 谁先执行是不确定的：
        ///   - 我们已经跑过：本帧接管过提交键 → 拦。
        ///   - 我们还没跑：键正处于按下状态、且我们有可激活的选中项 → 拦。
        ///
        /// 我们自己激活控件时引发的推进要放行 —— 那个按钮本来就是干这个的
        /// （例如全屏热区按钮）。
        /// </summary>
        internal static bool BlockGameAdvance
        {
            get
            {
                if (_inOurActivation) return false;

                try { if (_submitHandledFrame == Time.frameCount) return true; }
                catch { return false; }

                try
                {
                    if (!Input.GetKeyDown(KeyCode.Return)
                        && !Input.GetKeyDown(KeyCode.KeypadEnter)
                        && !Input.GetKeyDown(KeyCode.Space)) return false;
                }
                catch { return false; }

                return CurrentItem() != null;
            }
        }

        // ================= 退出确认 =================
        //
        // 只针对「按一下就把游戏关掉、而游戏自己不给确认框」的那一个控件：
        // 标题画面角落里的 EXIT。
        //
        // 标题画面有两个同类按钮，美术字分别是 START 和 EXIT，只差一个单词，
        // 而区分它们真正靠的是「哪一个是整块大面板、哪一个是角落小图标」——
        // 这种视觉信息读屏拿不到。按错一次整个会话直接没了。
        //
        // 游戏只在标题画面这一处不给确认框：剧情中的「返回标题」、手机菜单的
        // 「退出游戏」游戏自己都会弹原生确认框，我们不能重复问。
        //
        // 靠**对象名精确匹配**，名字是从游戏资源里实查的，不是猜的：
        //   标题场景 level1        StartButton / ExitButton / Title / QuitGame
        //   剧情场景 level3-5      MenuButton / QuitGame / Exit / ...   ← 没有 ExitButton
        // 完整版与试玩版都是这个结果，所以「名字 == ExitButton」只命中标题那一个。
        //
        // 踩过的坑（v0.5.4）：用正则 \b(exit|quit)\b 匹配，结果反了 ——
        //   ExitButton / QuitGame 是驼峰拼接，单词后面紧跟字母，根本没有 \b 词边界，
        //   于是标题那个 ExitButton 没命中；反而命中了剧情里名字就叫 Exit 的按钮
        //   （手机菜单的「退出游戏」，游戏自己有确认框）。
        //   教训：这种判定别用词边界，也别用「包含」，直接拿实查到的名字比。

        private static Component _pendingQuit;
        private static float _pendingQuitAt;
        private const float QuitConfirmSeconds = 8f;

        private static bool NeedsQuitConfirm(Component s)
        {
            if (Plugin.CfgQuitConfirm == null || !Plugin.CfgQuitConfirm.Value) return false;
            try
            {
                string cfg = Plugin.CfgQuitNames != null ? Plugin.CfgQuitNames.Value : "ExitButton";
                if (string.IsNullOrEmpty(cfg)) return false;

                string name = s.gameObject.name ?? "";
                string[] wants = cfg.Split(new char[] { ',', '，' });
                for (int i = 0; i < wants.Length; i++)
                {
                    string want = wants[i].Trim();
                    if (want.Length == 0) continue;
                    if (string.Equals(name, want, StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }
            catch { return false; }
        }

        private static bool QuitConfirmArmed(Component s)
        {
            if (_pendingQuit == null) return false;
            if (Time.realtimeSinceStartup - _pendingQuitAt > QuitConfirmSeconds)
            {
                ClearQuitConfirm();
                return false;
            }
            return _pendingQuit == s;
        }

        private static void ArmQuitConfirm(Component s)
        {
            _pendingQuit = s;
            _pendingQuitAt = Time.realtimeSinceStartup;
            // 留痕：万一配错了名字，日志里能看出到底拦的是哪个控件。
            try
            {
                Plugin.L.LogInfo("[UiNav] 退出确认：「" + s.gameObject.name + "」标签「"
                    + TextOf(s) + "」场景 " + SceneManager.GetActiveScene().name);
            }
            catch { }
            Speech.Speak("这是退出游戏。再按一次回车或空格确认退出，按别的键取消。", true);
        }

        private static void ClearQuitConfirm()
        {
            _pendingQuit = null;
        }

        private static void Activate(Component item)
        {
            if (item == null) { ExitInternal(false); return; }

            // 纯文本项（回想面板里的台词）没有可点的东西：
            // 回车就等于「再念一遍」，这也正是玩家想要的「重读上一句」。
            if (As<Selectable>(item) == null)
            {
                Speech.Speak(TextOf(item), true);
                return;
            }

            // 留痕：崩溃排查用。原生崩溃不会在日志里留下任何异常，
            // 只有我们自己事前写下的这一行能指明最后碰的是哪个控件。
            try
            {
                Plugin.L.LogInfo("[UiNav] 激活 " + item.GetType().Name + "「" + TextOf(item) + "」场景 "
                    + SceneManager.GetActiveScene().name);
            }
            catch { }

            var sel = As<Selectable>(item);
            if (sel != null && !sel.interactable)
            {
                Speech.Speak("该项当前不可用。", true);
                return;
            }

            try
            {
                TMP_InputField inf = As<TMP_InputField>(item);
                if (inf != null)
                {
                    // 先退出导航（ReleaseSelection 会清掉上一个高亮），
                    // 再把焦点交给输入框，否则刚设的焦点会被自己清掉。
                    ExitInternal(false);
                    SelectByUs(inf.gameObject);
                    inf.ActivateInputField();
                    Speech.Speak("已进入输入框，直接打字即可。按 Tab 返回导航。", true);
                    return;
                }

                Toggle t = As<Toggle>(item);
                if (t != null)
                {
                    t.isOn = !t.isOn;
                    Speech.Speak(t.isOn ? "开" : "关", false);
                    RequestRescanNextFrame();
                    return;
                }

                Slider sl = As<Slider>(item);
                if (sl != null)
                {
                    Speech.Speak("滑条请用左右方向键调整。", true);
                    return;
                }

                Button b = As<Button>(item);
                if (b != null)
                {
                    string label = TextOf(item);
                    Speech.Speak("已激活 " + label, false);
                    _inOurActivation = true;
                    try { b.onClick.Invoke(); }
                    finally { _inOurActivation = false; }
                    // 下一帧重扫：此刻旧场景仍完整存活；
                    // 若该按钮触发场景切换，离真正卸载还有几十帧
                    RequestRescanNextFrame();
                    return;
                }

                _inOurActivation = true;
                try
                {
                    ExecuteEvents.Execute(item.gameObject, new BaseEventData(EventSystem.current),
                        ExecuteEvents.submitHandler);
                }
                finally { _inOurActivation = false; }
                RequestRescanNextFrame();
            }
            catch (Exception e)
            {
                Plugin.L.LogError("激活控件失败: " + e.Message);
            }
        }

        private static void RequestRescanNextFrame()
        {
            _pendingRescanFrame = Time.frameCount + 1;
        }

        /// <summary>再排一次稍晚的重扫：面板滑入/淡入时，下一帧新控件往往还没激活。</summary>
        private static void RequestRescanDelayed()
        {
            _pendingRescanFrame2 = Time.frameCount + 14;
        }

        /// <summary>两份控件清单是不是一模一样（顺序也要一样）。</summary>
        private static bool SameList(List<Component> a, List<Component> b)
        {
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (!ReferenceEquals(a[i], b[i])) return false;
            }
            return true;
        }

        private static void Adjust(float dir)
        {
            if (Items.Count == 0) return;
            Slider sl = As<Slider>(Items[_index]);
            if (sl == null) return;
            try
            {
                float step = (sl.maxValue - sl.minValue) / 20f;
                if (sl.wholeNumbers) step = Mathf.Max(1f, Mathf.Round(step));
                sl.value = Mathf.Clamp(sl.value + dir * step, sl.minValue, sl.maxValue);
                Speech.Speak(SliderValueText(sl), false);
            }
            catch (Exception e) { Plugin.L.LogError("调整滑条失败: " + e.Message); }
        }

        // ================= 每帧 =================

        public static void Update()
        {
            // ---- 场景切换防护：必须放在最前面 ----
            try
            {
                Scene sc = SceneManager.GetActiveScene();
                if (sc.handle != _lastSceneHandle)
                {
                    _lastSceneHandle = sc.handle;
                    _sceneChangedAt = Time.realtimeSinceStartup;
                    if (_active || Groups.Count > 0) ExitInternal(false);
                    return;
                }
            }
            catch { }

            // ---- 激活后的下一帧重扫（唯一允许的自动扫描）----
            bool rescanDue =
                (_pendingRescanFrame >= 0 && Time.frameCount >= _pendingRescanFrame) ||
                (_pendingRescanFrame2 >= 0 && Time.frameCount >= _pendingRescanFrame2);
            if (rescanDue)
            {
                if (_pendingRescanFrame >= 0 && Time.frameCount >= _pendingRescanFrame) _pendingRescanFrame = -1;
                if (_pendingRescanFrame2 >= 0 && Time.frameCount >= _pendingRescanFrame2) _pendingRescanFrame2 = -1;
                if (_active)
                {
                    if (!SceneStable()) { ExitInternal(false); return; }
                    Component before = _announcedItem;
                    var beforeList = new List<Component>(Items);
                    Scan();
                    if (Groups.Count == 0 || Items.Count == 0) { ExitInternal(false); return; }
                    _index = Mathf.Clamp(_index, 0, Items.Count - 1);

                    // 列表重建后 _index 还停在原来的序号上，但那个位置上可能已经换了
                    // 别的控件（弹窗、二级菜单、翻页都会这样）。这时必须重新播报，
                    // 否则玩家按下去的是他从没听过的东西 —— 主菜单 START/EXIT 那类
                    // 只差一个单词的按钮，听错一次就出事。
                    //
                    // 注意还要比**整份清单**：打开子菜单时，当前这一项往往还是原来
                    // 那个按钮（序号也没变），但列表里多了新控件。只比当前项的话
                    // 就一声不吭，玩家以为界面没变。实测就是这样：按了 1/7 没有提示，
                    // 直到按 Esc 才听见「界面已更新」。
                    Component now = CurrentItem();
                    if (now != before || !SameList(beforeList, Items)) Announce("界面已更新。");
                }
            }

            if (Input.GetKeyDown(KeyCode.Tab)) { Toggle(); return; }

            // 游戏**自己**换面板时（例如姓名输入按回车 →「确定要使用这个姓名吗」
            // 确认框：promptUI 与 confirmationUI 直接 SetActive 互换）不会通知我们，
            // 列表会一直停在旧控件上，新面板的按钮就「定位不到」。
            // 当前项一旦失效就重扫一次；每个失效对象只请求一次，避免反复重扫。
            // ★ 当前项失效就重扫 —— **"当前项为 null" 也必须重扫**（这里栽过一次）：
            //   选项框（ChoiceHandlerPanel）的按钮是**池化复用**的，
            //   扫描那一刻抓到的那个 Button 下一帧就可能被换掉/销毁，
            //   于是 CurrentItem() 返回 Unity 伪空 → 回车那一支直接 return（按键原样交还游戏），
            //   既没反应、也没提示。实测日志就是这个形状：
            //     扫描里明明有「#1 （翻开笔记本），按钮」，但整局没有一行「激活 Selectable」——
            //   玩家报告原话：「用界面导航模式点击选项是没有用的，只能用数字键选择」
            //   （数字键那条路每帧重扫容器，所以一直是好的）。
            if (_active && _pendingRescanFrame < 0 && Time.realtimeSinceStartup >= _nextDeadRescan)
            {
                Component cur = CurrentItem();
                bool dead = cur == null
                    || cur.gameObject == null
                    || !cur.gameObject.activeInHierarchy;
                if (dead)
                {
                    _nextDeadRescan = Time.realtimeSinceStartup + 0.4f;
                    _rescanRequestedFor = cur;
                    RequestRescanNextFrame();
                }
            }

            // 全部控件失效时先退出导航，把按键还给游戏
            if (_active && (Groups.Count == 0 || Items.Count == 0)) ExitInternal(false);

            // ---- 回车 / 空格：整块状态机唯一的判定点 ----
            //
            //   非导航模式            CurrentItem() 为 null → 什么都不做，游戏自己推进剧情
            //   导航模式 + 没有可用项  CurrentItem() 为 null → 同上
            //   导航模式 + 有可用项    → 我们接管，激活它，并拦住游戏同一帧的推进
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)
                || Input.GetKeyDown(KeyCode.Space))
            {
                Component target = CurrentItem();
                if (target == null && _active)
                {
                    // 导航模式开着、列表里也有项，却拿不出"当前项" —— 说明那个对象已经死掉
                    // （Unity 伪空；池化复用的控件最常见）。留一行日志，
                    // 免得又变成"按了没反应、日志里什么都没有"这种最难查的形态。
                    Plugin.Diag("NAV 回车：当前项已失效（列表 " + Items.Count + " 项），按键交还游戏");
                }
                if (target != null)
                {
                    _submitHandledFrame = Time.frameCount;

                    // 标题画面的 EXIT 一按就关游戏，而游戏自己不给确认框。
                    // 读屏玩家分不清它和旁边的 START（美术字，只差一个单词），
                    // 所以这里补一道二次确认。详见 NeedsQuitConfirm。
                    if (NeedsQuitConfirm(target) && !QuitConfirmArmed(target))
                    {
                        ArmQuitConfirm(target);
                        return;
                    }
                    ClearQuitConfirm();

                    Activate(target);

                    // 激活之后界面很可能已经变了（打开子面板、弹出确认框、切页……），
                    // 而游戏自己换面板**不会**通知我们。所以无条件排两次重扫：
                    // 下一帧一次，稍晚再一次（面板有动画时用得上）。
                    // 清单没变就不会播报，多扫一次没有副作用。
                    RequestRescanNextFrame();
                    RequestRescanDelayed();
                }
                return;
            }

            if (!_active) return;

            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                _index = (_index - 1 + Items.Count) % Items.Count;
                Announce(null);
            }
            else if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                _index = (_index + 1) % Items.Count;
                Announce(null);
            }
            else if (Input.GetKeyDown(KeyCode.PageDown)) SwitchGroup(1);
            else if (Input.GetKeyDown(KeyCode.PageUp)) SwitchGroup(-1);
            else if (Input.GetKeyDown(KeyCode.LeftArrow)) Adjust(-1f);
            else if (Input.GetKeyDown(KeyCode.RightArrow)) Adjust(1f);
            else if (Input.GetKeyDown(KeyCode.Home)) { _index = 0; Announce(null); }
            else if (Input.GetKeyDown(KeyCode.End)) { _index = Items.Count - 1; Announce(null); }
        }
    }
}
