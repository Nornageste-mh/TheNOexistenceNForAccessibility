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
    /// 菜单 / 存档 / 设置等界面的键盘操作与朗读。
    ///
    /// === 操作（没有「导航模式」开关）===
    ///
    ///   界面一出现，键盘直接可用，不需要先按任何键「进入」。
    ///
    ///   上 / 下      上一项 / 下一项
    ///   Home / End   第一项 / 最后一项
    ///   回车 / 空格  激活当前项（按钮点击、开关切换、输入框聚焦）
    ///   左 / 右      当前项是滑条时调值（按住了连续调整）
    ///   退格        重读当前项（由 Plugin 的「重读当前项」入口调用 RepeatCurrent）
    ///
    ///   对话界面（HUD：设置、回想两个按钮）不自动接管 —— 自动接管会把
    ///   「回车推进剧情」抢走，而 HUD 上只有鼠标按钮。用「对话界面按钮热键」
    ///   （默认 Alt+M）进 / 出（ToggleHud）。
    ///
    /// === 接管与交还（状态机）===
    ///
    ///   空闲时每 0.35 秒扫一次：扫到「可见、可交互的一屏控件」，并且持续
    ///   0.2 秒，才认为界面打开了、开始接管（IdleDetect → Engage）。
    ///   界面隐藏 / 控件清空 / 场景切换 → 交还游戏（Disengage）。
    ///   同一页面 3 秒内重新出现 → 静默恢复，不重复报名。
    ///   输入框拿到焦点时一个键都不接管，让玩家安心打字。
    ///
    /// === 回车 / 空格的归属 ===
    ///
    ///   未接管                    → 不归我们，交给游戏推进剧情
    ///   已接管 + 当前项还活着      → 归我们，激活当前项，并拦住游戏本帧的推进
    ///   已接管 + 当前项没了        → 当场重扫；重扫完仍没有就交还游戏
    ///
    ///   判定依据是「本帧开始时是否在接管」，不是「EventSystem 里有没有
    ///   选中对象」—— 后者会被鼠标点击和游戏自己的焦点逻辑干扰，不能用来
    ///   决定按键归属。
    ///
    /// === 设计约束（每条都对应一次实测故障）===
    ///
    /// A) 【只操作最上层面板组】
    ///    实测存档界面一次能扫到 13~20 个控件，其中只有 5 个是存档槽，
    ///    其余是设置页签、画廊等无关按钮，读屏用户要在一堆噪声里找目标。
    ///    现在按「Canvas 下的顶层祖先」分组，只接管渲染层级最高的那一组
    ///    （列表恒取 Groups[0]），不再提供手动切组键。
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
    /// D) 【重扫必须按帧、且过场景门禁】
    ///    v0.3.0 曾在激活按钮后 0.35 秒重扫界面。「开始游戏」「读档」会立刻
    ///    SceneManager.LoadSceneAsync，重扫恰好落在场景销毁/激活瞬间，
    ///    对正在销毁的 Selectable 调用 FindObjectsOfTypeAll 会触发
    ///    无 C# 异常的原生崩溃。
    ///    现在激活后只排「下一帧」和「14 帧后」两次重扫（旧场景仍完整存活），
    ///    且必须通过 SceneStable 门禁；接管期间另有每 1 秒一次的重扫，也只
    ///    用于发现页面更换。
    ///
    /// E) 【恢复视觉反馈】
    ///    用 EventSystem.SetSelectedGameObject 让游戏自己的按钮高亮态生效。
    ///
    /// F) 【按键归属显式化；uGUI 的 submit 只在接管期间关上】
    ///    实测：退出操作后按空格推进剧情，会把上一次操作到的那个按钮又点
    ///    一遍。原因是 EventSystem.currentSelectedGameObject 在我们退出后
    ///    仍然指着那个按钮，而 StandaloneInputModule 一见回车/空格就向它发
    ///    submit。鼠标点过的按钮同理（Selectable.OnPointerDown 会自动选中自己）。
    ///
    ///    现在改成两件事：
    ///      1) 只在接管期间把 EventSystem.sendNavigationEvents 置 false
    ///         （见 SuppressUguiNav），交还时原样还原 —— 不再全局常驻关闭；
    ///      2) 回车/空格在我们自己手里时才算「提交」（见 Update 里的状态机）：
    ///         游戏自身那次「继续/提交」由 Naninovel 的输入屏蔽挡住
    ///         （AddBlockingUI "Continue"/"Submit"，见 BlockPageGameInput）。
    ///
    ///    为什么关掉 uGUI 的 submit 是安全的：全游戏代码里没有任何一处
    ///    SetSelectedGameObject，游戏自己并不依赖 EventSystem 选中态；
    ///    它的剧情推进、输入框确认、Esc 返回全部是自己读 Input.GetKeyDown。
    ///    鼠标点按走 pointer 事件，不受这个开关影响。
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
        private static readonly List<Component> EmptyItems = new List<Component>();

        /// <summary>
        /// 当前页面包含的组：同一个「界面根」（TitleUI / SettingsUI / NewCGGalleryUI …）
        /// 下的所有组。
        ///
        /// 为什么要合并：游戏的一个界面常常横跨多个 Canvas 组 ——
        ///   · 标题画面：按钮在 ButtonsPanel，「不」字那条隐藏滑条在 LogoPanel；
        ///   · CG 画廊：格子与翻页在 CGPanel，**返回按钮在 Top**。
        /// 只取最上面那一组，这些控件就永远进不了导航（实测玩家困死在画廊）。
        /// </summary>
        private static readonly List<Group> PageGroups = new List<Group>();

        /// <summary>当前页面的导航项：PageGroups 的 Items 依次拼接（组已按前后遮挡排序，组内已按位置排序）。</summary>
        private static readonly List<Component> PageItems = new List<Component>();

        private static List<Component> Items
        {
            get { return PageItems; }
        }

        // 界面操作此刻是否接管（取代原来的「Tab 导航模式」开关）
        private static bool _active;
        private static int _index;
        private static int _pendingRescanFrame = -1;
        // 稍晚再补一次重扫：面板常常有滑入/淡入动画，下一帧新控件可能还没激活
        private static int _pendingRescanFrame2 = -1;

        // 视觉反馈用：我们自己借 EventSystem 选中过的对象，退出时要清掉
        private static GameObject _selectedByUs;
        // 上一次朗读过的控件。重扫后如果这个位置换了别的控件，必须重新播报 ——
        // 否则玩家以为还停在刚才听的那一项上，按下去却是另一个东西。
        private static Component _announcedItem;

        // 当前项失效时我们请求过一次重扫，记下来免得每帧都重扫
        private static Component _rescanRequestedFor;

        // 「当前项已经死掉」这类失效的重扫节流（见 Update 里那一段）
        private static float _nextDeadRescan;

        // 滑条按住连续调整：首延迟 0.45 秒，之后每 0.12 秒一步（0→100% 不用按 20 次）
        private static int _repDir;
        private static float _repAt;
        private const float RepeatFirstDelay = 0.45f;
        private const float RepeatInterval = 0.12f;

        // 场景切换防护
        private static int _lastSceneHandle = int.MinValue;
        private static float _sceneChangedAt = float.NegativeInfinity;
        private const float SceneSettleSeconds = 1.5f;

        // ---- 自动接管界面（无模式键）----
        private static float _nextIdleScan;            // 下一次「空闲扫描」的时间
        private static float _lastUserInputAt;         // 玩家最后一次按键的时间（空闲扫描自适应退避用）
        private static float _candidateSince = -1f;    // 候选页面第一次被看到的时间
        private static string _candidateKey = "";      // 候选页面身份（要连续两次扫描一致才接管）
        private static int _candidateScans;            // 已经连续看到候选页面的次数
        private static string _excludedModalRoot = ""; // 被排除的模态界面根名（存档UI/选项/开发者面板…）
        private static int _excludedModalOrder = int.MinValue;   // 它的 Canvas 排序号（判断是不是盖在最上面）
        private static string _blockedNote = "";       // 上次播过的「被排除模态」日志（防 0.35 秒一行）
        private static Transform _pageRoot;            // 当前页面的分组根
        private static string _pageKey = "";           // 当前页面身份（控件公共祖先的路径）
        private static string _pageLabel = "";         // 当前页面的人话名字
        private static float _nextEngagedScan;         // 接管期间的定期重扫（捕捉鼠标引起的界面变化）
        private static float _settleUntil;             // 刚接管后的静默期（面板动画未稳，不播「界面已更新」）
        private static float _lastDumpAt = float.NegativeInfinity;   // 上次写详细诊断清单的时间（限流用）
        private static float _nextInputFieldScan;      // 输入框兜底扫描节流（防每帧 FindObjectsOfTypeAll）
        private static bool _inputFieldFallback;       // 上次兜底扫描的结果
        private static float _lastExitAt = float.NegativeInfinity;
        private static string _lastExitKey = "";

        // uGUI 导航总开关：只在接管期间关掉，退出时原样还原
        private static bool _navEventsSaved;
        private static bool _navEventsPrev = true;

        // 「已激活」延迟播报：按钮动作没有换页面时才补一声，不抢新页面的播报
        private static string _pendingActivationLabel;
        private static string _pendingActivationPage = "";
        private static float _pendingActivationAt = -1f;

        // 手动接管「对话界面按钮」（游戏只给了鼠标入口，见 ToggleHud）
        private static bool _hudMode;

        // HUD 扫描开关：HudEnter 扫描时让 NonMenu 放行 ControlUI；
        // 平时自动接管仍把 ControlUI 排除在外（否则回车会抢去点 HUD、剧情推不动）。
        private static bool _scanHud;

        // 剧情期纯键盘游玩时，游戏根本没把 HUD（ControlUI）实例化出来 —— 日志里它只以
        // 「[未激活] {}(资源) ControlUI/Buttons/HistoryButton」这种 prefab 资源形式存在。
        // 所以按热键时先请引擎把它显示出来，再在 1.5 秒内反复重扫，等它实例化/淡入。
        private static float _hudRetryUntil = -1f;
        private static float _hudNextTry;

        // 我们手动实例化出来的 HUD：退出界面操作时还原（避免游戏本来没显示 HUD，我们却一直挂着）。
        private static Naninovel.UI.IManagedUI _hudOwnedUi;
        private static bool _hudOwned;
        private static float _hudPrefabTriedAt = -1f;
        private static bool _hudAsyncStarted;

        // 接管期间把当前页面登记成「挡住 Continue/Submit」的 Naninovel UI（引擎官方机制）：
        // 这样回车/空格只激活我们的控件，不会顺手推进一句剧情；Esc（Cancel）不受影响。
        private static Naninovel.UI.IManagedUI _blockedUi;
        private static bool _blockedFallback;
        private static bool _prevProcessInput = true;

        // 滑条连发播报节流：值每 0.12 秒变一步，语音每 3 步念一次、松键再念终值
        private static int _repSteps;
        private static bool _repActive;
        private static bool _repEndSaid;

        public static bool Active { get { return _active; } }
        internal static bool HudMode { get { return _hudMode; } }

        // ================= 自动接管 / 交还 =================

        /// <summary>
        /// 对话期的界面按钮（设置、回想）被排除在自动接管之外（否则回车会被
        /// 抢去点按钮，剧情就推不动了），但游戏又没有键盘入口，所以给一个
        /// 显式功能键：按一下接管这一屏按钮，再按一下交还。
        /// </summary>
        public static void ToggleHud()
        {
            if (_active && _hudMode) { Disengage(true); return; }
            HudEnter();
        }

        private static void HudEnter()
        {
            // 对话 HUD（ControlUI）平时在「非菜单界面」排除名单里 —— 那是为了别自动
            // 抢走回车；手动按了热键就临时放行这一组，只收 ControlUI。
            _scanHud = true;
            try { Scan(); }
            finally { _scanHud = false; }

            if (Groups.Count > 0 && Items.Count > 0)
            {
                Engage(true);
                return;
            }

            // 纯键盘游玩时游戏从没把 ControlUI 实例化出来（日志里它只以 prefab 资源
            // 形式存在），所以这里先请引擎把这块 UI 显示出来。显示是异步的，同一帧
            // 扫不到，接下来 1.5 秒内反复重扫。
            if (TryRevealGameHud())
            {
                float now = Time.realtimeSinceStartup;
                _hudRetryUntil = now + 1.5f;
                _hudNextTry = 0f;
                Speech.Speak("正在打开对话界面按钮。", false);
                return;
            }
            Speech.Speak("当前没有可以操作的界面按钮。", true);
        }

        /// <summary>1.5 秒重试窗口：HUD 扫到就接管，超时播一句明确的话。</summary>
        private static void HudRetry()
        {
            float now = Time.realtimeSinceStartup;
            if (now >= _hudRetryUntil)
            {
                _hudRetryUntil = -1f;
                Speech.Speak("没能打开对话界面按钮。", true);
                return;
            }
            if (now < _hudNextTry) return;
            _hudNextTry = now + 0.2f;

            _scanHud = true;
            try { Scan(); }
            finally { _scanHud = false; }

            if (Groups.Count > 0 && Items.Count > 0)
            {
                _hudRetryUntil = -1f;
                Engage(true);
            }
        }

        /// <summary>
        /// 请引擎把对话 HUD（ControlUI / ControlPanel）显示出来。
        /// 先按配置名取 UI 实例并 Show()，失败再走引擎自己的「显示全部 UI」入口。
        /// 任何一步失败都只记日志：HUD 只是便利入口，不能因此影响游戏本身。
        /// </summary>
        private static bool TryRevealGameHud()
        {
            try
            {
                var im = Naninovel.Engine.GetService<Naninovel.IUIManager>();
                if (im == null) { HudLog("拿不到 IUIManager"); return false; }
                HudLog("开始显示尝试（GetUI → AddUIAsync 实例化 → 兜底）");

                string[] names = new string[] { "ControlUI", "ControlPanel", "ControlsUI", "Control" };
                for (int i = 0; i < names.Length; i++)
                {
                    Naninovel.UI.IManagedUI ui = null;
                    try { ui = im.GetUI(names[i]); }
                    catch { ui = null; }
                    if (ui == null) continue;
                    if (SetUiVisible(ui))
                    {
                        HudLog("已通过 GetUI(\"" + names[i] + "\") 显示");
                        return true;
                    }
                }

                // RE 结论：GetUI 只查不建；实例化唯一正路是 UIManager.AddUIAsync
                // （内部 Object.Instantiate → GetComponent<CustomUI> → ManagedUI 注册 → 初始化）。
                if (TryInstantiateHudAsync())
                {
                    HudLog("已发起 AddUIAsync 实例化，等待回调");
                    return true;
                }

                // 异步路线起不来时再赌一把裸实例化（protected InstantiatePrefab，反射调用）。
                var fresh = InstantiateHudFromPrefab();
                if (fresh != null && SetUiVisible(fresh))
                {
                    _hudOwnedUi = fresh;
                    _hudOwned = true;
                    HudLog("已从 prefab 实例化并显示");
                    return true;
                }


                // 不再用 SetUIVisibleWithToggle 兜底：RE 确认它会把摄像机 UI 层一起关掉，
                // 副作用远大于收益（可能把所有 UI 弄没）。到这里就是失败，只留诊断。

                DumpHudDiagnostics(im);
            }
            catch (Exception e) { HudLog("显示尝试失败 " + e.Message); }
            return false;
        }

        /// <summary>
        /// 异步创建：UIManager.AddUIAsync 会「实例化 + 登记 + 初始化」，比反射调 InstantiatePrefab 完整。
        /// 用引擎自带的 UniTaskExtensions.ToCoroutine 挂到 MonoBehaviour 上跑，回调里 Show 出来。
        /// </summary>
        private static bool TryInstantiateHudAsync()
        {
            if (_hudAsyncStarted) return true;   // 已发起，等回调
            var runner = NoExistenceA11y.A11yBehaviour.Instance;
            if (runner == null) { HudLog("AddUIAsync：没有 MonoBehaviour 宿主"); return false; }
            var prefab = FindUiPrefab("ControlUI");
            if (prefab == null) prefab = FindUiPrefab("ControlPanel");
            if (prefab == null) { HudLog("AddUIAsync：内存里没有 ControlUI prefab"); return false; }
            var im = Naninovel.Engine.GetService<Naninovel.IUIManager>();
            if (im == null) { HudLog("AddUIAsync：拿不到 IUIManager"); return false; }
            try
            {
                var task = im.AddUIAsync(prefab, "ControlUI", null);
                var onOk = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Naninovel.UI.IManagedUI>>(
                    new System.Action<Naninovel.UI.IManagedUI>(OnHudAsyncDone));
                var onErr = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Il2CppSystem.Exception>>(
                    new System.Action<Il2CppSystem.Exception>(ex => HudLog("AddUIAsync 失败 " + (ex == null ? "?" : ex.Message))));
                var co = Naninovel.UniTaskExtensions.ToCoroutine<Naninovel.UI.IManagedUI>(task, onOk, onErr);
                runner.StartCoroutine(co);
                _hudAsyncStarted = true;
                return true;
            }
            catch (Exception e) { HudLog("AddUIAsync 发起失败 " + e.Message); return false; }
        }

        private static void OnHudAsyncDone(Naninovel.UI.IManagedUI ui)
        {
            try
            {
                _hudAsyncStarted = false;
                if (ui == null) { HudLog("AddUIAsync 回调为空"); return; }
                if (SetUiVisible(ui))
                {
                    _hudOwnedUi = ui;
                    _hudOwned = true;
                    _hudRetryUntil = Time.realtimeSinceStartup + 1.5f;
                    _hudNextTry = 0f;
                    HudLog("AddUIAsync 完成并显示");
                }
            }
            catch (Exception e) { HudLog("AddUIAsync 回调处理失败 " + e.Message); }
        }

        /// <summary>在内存里找名为 name 的 prefab 资源（不是场景实例）。</summary>
        private static GameObject FindUiPrefab(string name)
        {
            try
            {
                var gos = Resources.FindObjectsOfTypeAll<GameObject>();
                if (gos == null) return null;
                for (int i = 0; i < gos.Length; i++)
                {
                    var go = gos[i];
                    if (go == null || go.name != name) continue;
                    if (go.scene.IsValid()) continue;   // 只要 prefab 资源
                    return go;
                }
            }
            catch (Exception e) { HudLog("找 prefab 失败 " + e.Message); }
            return null;
        }

        /// <summary>
        /// 游戏在纯键盘剧情期从不实例化 ControlUI；这里调 UIManager 的 protected InstantiatePrefab
        /// 把已加载的 prefab 资源做成受引擎管理的 UI，然后由调用方 Show 出来。
        /// </summary>
        private static Naninovel.UI.IManagedUI InstantiateHudFromPrefab()
        {
            if (_hudOwned && _hudOwnedUi != null) return _hudOwnedUi;

            float now = Time.realtimeSinceStartup;
            if (_hudPrefabTriedAt > 0f && now - _hudPrefabTriedAt < 8f) return null;   // 别每次按键都试
            _hudPrefabTriedAt = now;

            try
            {
                var prefab = FindUiPrefab("ControlUI");
                if (prefab == null) prefab = FindUiPrefab("ControlPanel");
                if (prefab == null)
                {
                    HudLog("内存里没有 ControlUI prefab");
                    return null;
                }

                var im = Naninovel.Engine.GetService<Naninovel.IUIManager>();
                if (im == null) return null;
                var mgr = ((Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)im).TryCast<Naninovel.UIManager>();
                if (mgr == null)
                {
                    HudLog("IUIManager 实际类型不是 UIManager");
                    return null;
                }

                var mi = typeof(Naninovel.UIManager).GetMethod("InstantiatePrefab",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (mi == null)
                {
                    HudLog("UIManager 上没有 InstantiatePrefab");
                    return null;
                }

                object raw = mi.Invoke(mgr, new object[] { prefab, "ControlUI", null });
                var ui = raw == null ? null : As<Naninovel.UI.IManagedUI>((UnityEngine.Object)raw);
                if (ui == null) HudLog("InstantiatePrefab 返回空");
                return ui;
            }
            catch (Exception e)
            {
                HudLog("实例化 prefab 失败 " + e.Message);
                return null;
            }
        }

        /// <summary>把任何一个 Naninovel UI 设成可见：引擎自带 ManagedUIExtensions.Show 会按具体类型派发。</summary>
        private static bool SetUiVisible(Naninovel.UI.IManagedUI ui)
        {
            try
            {
                Naninovel.ManagedUIExtensions.Show(ui);
                return true;
            }
            catch (Exception e)
            {
                HudLog("Show 失败 " + e.Message);
                return false;
            }
        }

        /// <summary>HUD 生命周期走 BepInEx 日志（不受「详细日志」开关影响），下次复测一眼能看出卡在哪步。</summary>
        private static void HudLog(string s)
        {
            try { if (Plugin.L != null) Plugin.L.LogInfo("[UiNav] HUD：" + s); } catch { }
        }

        /// <summary>HUD 显示失败时把现场写进日志：下次玩家复测就能看出卡在哪一步。</summary>
        private static void DumpHudDiagnostics(Naninovel.IUIManager im)
        {
            try
            {
                var sb = new System.Text.StringBuilder("诊断：");
                sb.Append(" HasUI(ControlUI)=").Append(im.HasUI("ControlUI"));
                var uis = im.GetManagedUIs();
                sb.Append(" 已实例化 UI 数=").Append(uis == null ? 0 : uis.Count);

                // IReadOnlyCollection 代理没有 GetEnumerator，换个角度看：内存里所有 CustomUI 行为。
                var behaviours = Resources.FindObjectsOfTypeAll<Naninovel.ScriptableUIBehaviour>();
                int total = behaviours == null ? 0 : behaviours.Length;
                int sceneOnes = 0;
                sb.Append(" 行为总数=").Append(total);
                if (behaviours != null)
                {
                    int shown = 0;
                    for (int i = 0; i < behaviours.Length; i++)
                    {
                        var b = behaviours[i];
                        if (b == null) continue;
                        bool inScene = b.gameObject.scene.IsValid();
                        if (inScene) sceneOnes++;
                        if (shown < 12 && inScene)   // 只列场景内的（已实例化），最多 12 个
                        {
                            sb.Append(" [").Append(b.gameObject.name).Append("]");
                            shown++;
                        }
                    }
                }
                sb.Append(" 场景内=").Append(sceneOnes);

                var gos = Resources.FindObjectsOfTypeAll<GameObject>();
                int hits = 0;
                if (gos != null)
                {
                    for (int i = 0; i < gos.Length && hits < 4; i++)
                    {
                        var go = gos[i];
                        if (go == null) continue;
                        if (go.name != "ControlUI" && go.name != "ControlPanel") continue;
                        hits++;
                        sb.Append(" {").Append(go.name).Append(go.scene.IsValid() ? "·场景" : "·资源")
                          .Append(go.activeInHierarchy ? "·active" : "·inactive");
                        var mbs = go.GetComponents<MonoBehaviour>();
                        if (mbs != null)
                            foreach (var c in mbs) { if (c != null) sb.Append(" ").Append(c.GetType().Name); }
                        sb.Append("}");
                    }
                }
                HudLog(sb.ToString());
            }
            catch (Exception e) { Plugin.Diag("[UiNav] HUD 诊断失败 " + e.Message); }
        }

        private static void Engage(bool hud)
        {
            if (Groups.Count == 0 || Items.Count == 0) return;

            _active = true;
            _hudMode = hud || PageIsHud();
            _index = 0;
            _pageRoot = Groups[0].Root;
            _pageKey = PageKey();
            _pageLabel = PageLabel();
            _nextEngagedScan = Time.realtimeSinceStartup + 1.0f;
            _settleUntil = Time.realtimeSinceStartup + 0.8f;   // 接管后 0.8 秒内不播「界面已更新」
            ResetRepeatState();
            SuppressUguiNav(true);
            BlockPageGameInput(true);

            DiagScan((hud ? "手动接管对话界面：" : "自动接管界面：")
                + _pageLabel + "，共 " + Items.Count + " 项");

            // 刚刚才退出同一个页面（例如关掉一个确认框又回到设置）→ 不重复报页面名，
            // 只报当前项，免得玩家以为又跳到了别的地方。
            bool silent = !hud && _pageKey == _lastExitKey
                && Time.realtimeSinceStartup - _lastExitAt < 3f;
            if (silent) Announce(null, false);
            else AnnouncePage(true, !hud);
        }

        private static void Disengage(bool announce)
        {
            bool was = _active;
            bool wasHud = _hudMode;
            _active = false;
            _hudMode = false;
            _index = 0;
            _settleUntil = 0f;
            _pendingRescanFrame = -1;
            _pendingRescanFrame2 = -1;
            _announcedItem = null;
            _pendingActivationAt = -1f;
            _pendingActivationLabel = null;
            _candidateSince = -1f;
            _candidateKey = "";
            _candidateScans = 0;
            ResetRepeatState();
            ClearQuitConfirm();
            ReleaseSelection();
            SuppressUguiNav(false);
            BlockPageGameInput(false);
            if (_hudOwned)
            {
                try { if (_hudOwnedUi != null) Naninovel.ManagedUIExtensions.Hide(_hudOwnedUi); }
                catch (Exception e) { HudLog("退出时隐藏失败 " + e.Message); }
                _hudOwned = false;
                _hudOwnedUi = null;
            }
            if (was)
            {
                _lastExitAt = Time.realtimeSinceStartup;
                _lastExitKey = _pageKey;
            }
            PageGroups.Clear();
            PageItems.Clear();
            _pageRoot = null;
            _pageKey = "";
            _pageLabel = "";
            _nextIdleScan = 0f;   // 刚交还键盘：下一帧就开始侦测，界面若还在就重新接管
            if (announce)
            {
                try { Speech.Speak("已退出界面操作。", !wasHud); } catch { }
            }
        }

        /// <summary>
        /// 把当前页面登记成「挡住 Continue/Submit」的 Naninovel UI，接管结束再撤销。
        ///
        /// 为什么需要：0.2.0.0 不再全局屏蔽游戏输入（旧导航模式那样做副作用太大，
        /// 连 Esc 都按不动）。但界面开着时，游戏自己的「继续/提交」采样仍可能被
        /// 同一个按键触发 —— 一次回车既激活按钮、又推进一句剧情。这里用引擎自己的
        /// AddBlockingUI 只挡 Continue/Submit 两个动作：Esc（Cancel）不受影响，
        /// 鼠标点击也不受影响。拿不到当前页的 IManagedUI 时，退回「临时关 ProcessInput」。
        /// </summary>
        private static void BlockPageGameInput(bool block)
        {
            try
            {
                Naninovel.IInputManager im = Naninovel.Engine.GetService<Naninovel.IInputManager>();
                if (im == null) return;
                if (block)
                {
                    if (_blockedUi != null || _blockedFallback) return;
                    _blockedUi = FindManagedUi(Groups.Count > 0 ? Groups[0].Root : null);
                    if (_blockedUi != null)
                    {
                        im.AddBlockingUI(_blockedUi, new string[] { "Continue", "Submit" });
                    }
                    else
                    {
                        _prevProcessInput = im.ProcessInput;
                        im.ProcessInput = false;
                        _blockedFallback = true;
                    }
                }
                else
                {
                    try
                    {
                        if (_blockedUi != null) im.RemoveBlockingUI(_blockedUi);
                        else if (_blockedFallback) im.ProcessInput = _prevProcessInput;
                    }
                    catch { }
                    _blockedUi = null;
                    _blockedFallback = false;
                }
            }
            catch
            {
                _blockedUi = null;
                _blockedFallback = false;
            }
        }

        internal static Naninovel.UI.IManagedUI FindManagedUi(Transform t)
        {
            try
            {
                if (t == null) return null;
                var mbs = t.GetComponentsInParent<MonoBehaviour>(true);
                for (int i = 0; i < mbs.Length; i++)
                {
                    var ui = As<Naninovel.UI.IManagedUI>(mbs[i]);
                    if (ui != null) return ui;
                }
            }
            catch { }
            return null;
        }

        private static void ResetRepeatState()
        {
            _repDir = 0;
            _repAt = 0f;
            _repSteps = 0;
            _repActive = false;
            _repEndSaid = false;
        }

        /// <summary>松键时补念一次终值（连发过程中为了不打断语音，每 3 步才念一次）。</summary>
        private static void SpeakSliderNow()
        {
            if (!_active || Groups.Count == 0 || Items.Count == 0) return;
            Component item = Items[Mathf.Clamp(_index, 0, Items.Count - 1)];
            Slider sl = As<Slider>(item);
            if (sl == null) return;
            string label = TextOf(item);
            if (string.IsNullOrEmpty(label)) label = "这一项";
            Speech.Speak(label + "，" + SliderValueText(sl) + "。", true);
        }


        /// <summary>重读当前项（退格键共用那个出口）。返回 false = 当前没接管界面，去重读台词。</summary>
        internal static bool RepeatCurrent()
        {
            if (!_active || Items.Count == 0) return false;
            Announce(null);
            return true;
        }

        // ================= 自动接管界面所需的判定 =================

        /// <summary>
        /// 接管界面期间关掉 uGUI 的「键盘导航/提交」通路，退出时还原。
        ///
        /// 为什么必须关：我们自己设了 EventSystem 选中态（为了让游戏自己的高亮生效），
        /// 而 StandaloneInputModule 会在回车/空格时向选中对象发一次 submit ——
        /// 我们同时又直接调 onClick，等于一次按键点两下。
        ///
        /// 只在接管期间关、退出还原（不再像旧版那样每帧常驻关闭）：
        /// 常驻关会让「关掉补丁行为不变」这个承诺落空，也会掩盖游戏自己的问题。
        /// </summary>
        private static void SuppressUguiNav(bool suppress)
        {
            try
            {
                EventSystem es = EventSystem.current;
                if (es == null) return;
                if (suppress)
                {
                    if (_navEventsSaved) return;
                    _navEventsPrev = es.sendNavigationEvents;
                    _navEventsSaved = true;
                    if (es.sendNavigationEvents) es.sendNavigationEvents = false;
                }
                else
                {
                    if (!_navEventsSaved) return;
                    es.sendNavigationEvents = _navEventsPrev;
                    _navEventsSaved = false;
                }
            }
            catch { _navEventsSaved = false; }
        }

        /// <summary>是否正有输入框在打字（供外部热键避开退格等编辑键）。</summary>
        internal static bool InputFieldActive { get { return InputFieldFocused(); } }

        /// <summary>输入框（TMP_InputField）拿到焦点时，我们一个键都不接管，让玩家安心打字。</summary>
        private static bool InputFieldFocused()
        {
            try
            {
                // 先看 EventSystem 当前选中：界面里可能同时有两个输入框（姓名界面），
                // FindObjectOfType 可能拿到另一个没聚焦的，导致「在打字却仍抢键」。
                EventSystem es = EventSystem.current;
                if (es != null)
                {
                    GameObject cur = es.currentSelectedGameObject;
                    if (cur != null)
                    {
                        TMP_InputField inf = cur.GetComponentInParent<TMP_InputField>();
                        if (inf != null && inf.isFocused) return true;
                    }
                }

                // 兜底：某些输入框没有走 EventSystem 选中态，扫一遍真正 isFocused 的那个。
                // ★ 这个兜底每帧调用一次 = 每帧 FindObjectsOfTypeAll，实测是卡顿来源之一，
                //   改为 0.5 秒最多扫一次并缓存结果（本作输入框都走 EventSystem，兜底概率极低）。
                float now = Time.realtimeSinceStartup;
                if (now < _nextInputFieldScan) return _inputFieldFallback;
                _nextInputFieldScan = now + 0.5f;
                _inputFieldFallback = false;
                var all = Resources.FindObjectsOfTypeAll<TMP_InputField>();
                for (int i = 0; i < all.Length; i++)
                {
                    TMP_InputField f = all[i];
                    if (f == null || f.gameObject == null) continue;
                    if (f.gameObject.activeInHierarchy && f.isFocused) { _inputFieldFallback = true; break; }
                }
                return _inputFieldFallback;
            }
            catch { return false; }
        }

        /// <summary>当前页面的分组根是否还活着（还显示着）。</summary>
        private static bool PageStillAlive()
        {
            try
            {
                if (PageGroups.Count == 0) return false;
                for (int i = 0; i < PageGroups.Count; i++)
                {
                    Transform root = PageGroups[i].Root;
                    if (root == null || root.gameObject == null) continue;
                    if (!root.gameObject.activeInHierarchy) continue;
                    if (!AlphaHidden(root)) return true;
                }
                return false;
            }
            catch { return false; }
        }

        /// <summary>
        /// 当前页面的身份：页面内所有控件的**公共祖先**路径。
        ///
        /// 不用分组根的原因：分组根是「Canvas 下的第一层孩子」（例如 UI / ModalUI），
        /// 一个根下可能同时住着好几个面板；而公共祖先能精确到面板本身，
        /// 用来判断「还是同一页吗」和「换页了吗」。
        /// </summary>
        private static string PageKey()
        {
            try
            {
                Transform common = null;
                // 先用本页各组根的公共祖先（结果就是 UI 根，例如 …/ModalUI/TitleUI），
                // 比只拿控件的公共祖先稳定：页内还有未显示的兄弟组时也不会算错。
                for (int i = 0; i < PageGroups.Count; i++)
                {
                    Transform rt = PageGroups[i].Root;
                    if (rt == null) continue;
                    common = common == null ? rt : CommonAncestor(common, rt);
                    if (common == null) break;
                }
                if (common != null) return PathOf(common);
                for (int i = 0; i < Items.Count; i++)
                {
                    Component c = Items[i];
                    if (c == null) continue;
                    Transform ct = c.transform;
                    if (ct == null) continue;
                    common = common == null ? ct : CommonAncestor(common, ct);
                    if (common == null) break;
                }
                if (common != null) return PathOf(common);
            }
            catch { }
            try
            {
                if (Groups.Count > 0 && Groups[0].Root != null) return PathOf(Groups[0].Root);
            }
            catch { }
            return "";
        }

        private static Transform CommonAncestor(Transform a, Transform b)
        {
            try
            {
                Transform x = a;
                int guard = 0;
                while (x != null && guard++ < 48)
                {
                    Transform y = b;
                    int guard2 = 0;
                    while (y != null && guard2++ < 48)
                    {
                        if (x == y) return x;
                        y = y.parent;
                    }
                    x = x.parent;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 从组根/控件的路径里取它所属的「界面根」名字：
        /// `Naninovel&lt;Runtime&gt;/ModalUI/&lt;Name&gt;/…` 或 `…/UI/&lt;Name&gt;/…` 里的 &lt;Name&gt;。
        /// 例：SettingsUI、TitleUI、NewCGGalleryUI、BacklogFakeUI、ControlUI。
        /// 认不出来返回空串（调用方退回只操作最上面那一组）。
        /// </summary>
        private static string UiRootOf(Transform t)
        {
            try
            {
                string path = PathOf(t);
                int m = path.IndexOf("/ModalUI/", StringComparison.OrdinalIgnoreCase);
                if (m >= 0) return NextSegment(path, m + "/ModalUI/".Length);
                int u = path.IndexOf("/UI/", StringComparison.OrdinalIgnoreCase);
                if (u >= 0) return NextSegment(path, u + "/UI/".Length);
                return "";
            }
            catch { return ""; }
        }

        private static string NextSegment(string path, int start)
        {
            if (start >= path.Length) return "";
            int end = path.IndexOf('/', start);
            return end < 0 ? path.Substring(start) : path.Substring(start, end - start);
        }

        /// <summary>
        /// 页面 = 排序后第一组所属的界面根下的所有组；其他界面根一律不算本页。
        /// 例：打开设置时，背景里还活着的 TitleUI 组会被排除，标题按钮不会混进设置页；
        /// 标题画面自己的 ButtonsPanel + LogoPanel 则会合成一页（「不」字滑条才可达）。
        /// </summary>
        private static void SelectPage()
        {
            PageGroups.Clear();
            PageItems.Clear();
            if (Groups.Count == 0) return;

            // ★ 被规则排除的模态界面（存档读档、选项、开发者面板、QTE/加载/电影…）
            //   如果它盖在当前页面上面，就一页都不给：绝不能去点背景界面里露出来的按钮
            //   （否则在存档面板上按回车会点穿到标题画面的 START）。
            //   注意 CanvasOrder 比较取「最上层被排除模态」与「当前最上层可操作组」。
            if (_excludedModalOrder > Groups[0].CanvasOrder)
            {
                if (_blockedNote != _excludedModalRoot)
                {
                    _blockedNote = _excludedModalRoot;
                    DiagScan("最上层是被排除的界面「" + _excludedModalRoot + "」，暂不接管（防点穿背景）");
                }
                return;
            }
            _blockedNote = "";

            string rootUi = UiRootOf(Groups[0].Root);
            for (int i = 0; i < Groups.Count; i++)
            {
                Group g = Groups[i];
                if (g.Root == null || g.Items.Count == 0) continue;
                if (!string.IsNullOrEmpty(rootUi) && UiRootOf(g.Root) != rootUi) continue;
                PageGroups.Add(g);
                PageItems.AddRange(g.Items);
            }

            // 认不出界面根（例如刚加载出来的裸 Canvas）时退回旧行为：只操作最上面那一组。
            if (PageGroups.Count == 0)
            {
                PageGroups.Add(Groups[0]);
                PageItems.AddRange(Groups[0].Items);
            }

            SortPage();   // ★ 跨组全局位置排序：读屏玩家靠「上下」建立空间印象，顺序必须和画面一致
        }

        /// <summary>
        /// 把合并后的整页项目按屏幕位置统一排序（同一页里可能来自多个 Canvas 组）。
        /// 相机按**该项所属组**取，不能统一用一个相机（Overlay 与相机模式混用会算错）。
        /// </summary>
        private static void SortPage()
        {
            if (PageItems.Count < 2) return;
            bool byPosition = Plugin.CfgUiSortByPosition == null || Plugin.CfgUiSortByPosition.Value;

            var keys = new List<ItemKey>(PageItems.Count);
            for (int i = 0; i < PageItems.Count; i++)
            {
                Component s = PageItems[i];
                Group owner = null;
                for (int gi = 0; gi < PageGroups.Count; gi++)
                {
                    if (PageGroups[gi].Items.Contains(s)) { owner = PageGroups[gi]; break; }
                }

                var k = new ItemKey { S = s, Row = int.MinValue, Left = float.MaxValue, Path = null };
                try
                {
                    RectTransform rt = s != null ? As<RectTransform>(s.transform) : null;
                    if (byPosition && rt != null && owner != null)
                    {
                        float top, left;
                        if (ScreenTopLeft(rt, owner.Camera, out top, out left))
                        {
                            k.Left = left;
                            k.Row = Mathf.RoundToInt(top / 4f);   // 4 像素一档，避免非传递比较
                        }
                    }
                    if (s != null && owner != null) k.Path = PathIndices(s.transform, owner.Root).ToArray();
                }
                catch { }
                keys.Add(k);
            }

            if (byPosition) keys.Sort(CompareKey);
            else keys.Sort(ComparePathOnly);

            PageItems.Clear();
            for (int i = 0; i < keys.Count; i++) PageItems.Add(keys[i].S);
        }

        /// <summary>整页有多少个「点得动」的控件（只读兜底进来的不算）。</summary>
        private static int UsableCountOfPage()
        {
            int n = 0;
            for (int i = 0; i < PageGroups.Count; i++) n += UsableCount(PageGroups[i]);
            return n;
        }

        /// <summary>页面的人话名字（从控件路径里认面板；认不出就叫「当前界面」）。</summary>
        private static string PageLabel()
        {
            if (_hudMode) return "对话界面按钮";
            try
            {
                string path = PageKey();
                if (path.IndexOf("BacklogFakeUI", StringComparison.OrdinalIgnoreCase) >= 0) return "笔记本";
                if (path.IndexOf("BacklogUI", StringComparison.OrdinalIgnoreCase) >= 0) return "回想";
                if (path.IndexOf("SettingsUI", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.IndexOf("GameSettings", StringComparison.OrdinalIgnoreCase) >= 0) return "设置界面";
                if (path.IndexOf("TitleUI", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.IndexOf("TitleMenu", StringComparison.OrdinalIgnoreCase) >= 0) return "标题画面";
                if (path.IndexOf("Confirmation", StringComparison.OrdinalIgnoreCase) >= 0) return "确认";
                if (path.IndexOf("SaveLoad", StringComparison.OrdinalIgnoreCase) >= 0) return "存档读档";
                if (path.IndexOf("PauseUI", StringComparison.OrdinalIgnoreCase) >= 0) return "暂停界面";
                if (path.IndexOf("CGGallery", StringComparison.OrdinalIgnoreCase) >= 0) return "画廊";
                if (path.IndexOf("TipsUI", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.IndexOf("TipsPanel", StringComparison.OrdinalIgnoreCase) >= 0) return "提示";
                if (path.IndexOf("ControlUI", StringComparison.OrdinalIgnoreCase) >= 0) return "对话界面按钮";
                if (path.IndexOf("Phone", StringComparison.OrdinalIgnoreCase) >= 0) return "手机";
            }
            catch { }
            return "当前界面";
        }

        /// <summary>当前页面是不是对话界面（HUD）那一组按钮。</summary>
        private static bool PageIsHud()
        {
            try
            {
                return PageKey().IndexOf("ControlUI", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// 这一屏控件属于「不是菜单的东西」（对话 HUD、字幕、跳过提示、载入、过场、QTE）。
        /// 它们不能自动接管：否则回车会被抢去点 HUD，剧情就推不动了。
        /// 名单来自配置，用路径片段匹配。
        /// </summary>
        private static bool NonMenu(Transform t)
        {
            try
            {
                string path = PathOf(t);
                bool hudPath = path.IndexOf("ControlUI", StringComparison.OrdinalIgnoreCase) >= 0;

                // HUD 平时被排除（不排除的话剧情期一切换就自动接管、回车会抢去点按钮）；
                // 只有玩家按 Alt+M（_scanHud）或正在操作 HUD（_hudMode）时才放它进来。
                if (hudPath) return !(_hudMode || _scanHud);

                var cfg = Plugin.CfgUiMenuExcludePaths;
                if (cfg == null || string.IsNullOrEmpty(cfg.Value)) return false;
                string[] frags = cfg.Value.Split(',');
                for (int i = 0; i < frags.Length; i++)
                {
                    string f = frags[i].Trim();
                    if (f.Length == 0) continue;
                    if (path.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// 「整屏大按钮」（点击穿透层 / 全屏推进热区）：看得见、但当作导航项毫无意义 ——
        /// 焦点停在它上面只会让玩家以为界面没东西可点。
        /// </summary>
        private static bool FullscreenButton(Component c)
        {
            try
            {
                if (As<Button>(c) == null) return false;
                RectTransform rt = AsRect(c.transform);
                if (rt == null) return false;
                Rect r = WorldRect(rt);
                return r.width >= Screen.width * 0.9f && r.height >= Screen.height * 0.9f;
            }
            catch { return false; }
        }

        /// <summary>延迟播报「已激活」：按钮真的换了页面时，新页面的播报已经说明问题。</summary>
        private static void TickPendingActivation()
        {
            if (_pendingActivationAt < 0f) return;
            if (Time.realtimeSinceStartup < _pendingActivationAt) return;
            string label = _pendingActivationLabel;
            string page = _pendingActivationPage;
            _pendingActivationAt = -1f;
            _pendingActivationLabel = null;
            if (!_active || string.IsNullOrEmpty(label)) return;
            if (page != _pageKey) return;      // 换页了：新页面已经播报过
            Speech.Speak("已激活「" + label + "」。", false);
        }

        /// <summary>空闲时的界面侦测：看到界面稳定出现，就自动接管。</summary>
        private static void IdleDetect()
        {
            float now = Time.realtimeSinceStartup;
            // 玩家刚按过键 → 立刻回到最勤快的扫描节奏（菜单可能是刚被按键叫出来的）；
            // 长时间没有输入（被动听剧情/自动阅读）→ 逐步退避，省下空转的扫描开销。
            if (Input.anyKeyDown) _lastUserInputAt = now;
            if (_lastUserInputAt <= 0f) _lastUserInputAt = now;   // 启动后第一次：按最勤快节奏来
            if (now < _nextIdleScan) return;
            float quiet = now - _lastUserInputAt;
            float interval = quiet < 0.8f ? 0.35f : (quiet < 3f ? 0.7f : 1.4f);
            _nextIdleScan = now + interval;

            if (!SceneStable()) { _candidateSince = -1f; return; }
            // 正在打字（输入框拿着焦点）时不去接管界面
            if (InputFieldFocused()) { _candidateSince = -1f; return; }

            Scan();
            if (Groups.Count == 0 || Items.Count == 0 || !PageStillAlive())
            {
                _candidateSince = -1f;
                _candidateKey = "";
                _candidateScans = 0;
                return;
            }

            string key = PageKey();
            // 面板淡入/滑入时控件会先出现、数量和位置还没稳定：要求**连续两次扫描**
            // 看到同一个页面再接管，避免「接管到只有一半控件的半成品界面」。
            if (_candidateSince < 0f || key.Length == 0 || key != _candidateKey)
            {
                _candidateSince = now;
                _candidateKey = key;
                _candidateScans = 1;
                return;
            }
            _candidateScans++;
            if (now - _candidateSince < 0.2f) return;
            if (_candidateScans < 2) return;
            Engage(false);
        }

        /// <summary>接管期间的重扫：激活后、定期、以及当前项失效时都会走这里。</summary>
        private static void RescanEngaged(bool activationRescan)
        {
            if (!SceneStable()) { Disengage(false); return; }

            string beforePage = _pageKey;
            Component before = _announcedItem;
            var beforeList = new List<Component>(Items);

            bool prevScanHud = _scanHud;
            if (_hudMode) _scanHud = true;   // HUD 接管中扫描要放 ControlUI 进来
            try { Scan(); } finally { _scanHud = prevScanHud; }
            if (Groups.Count == 0 || Items.Count == 0)
            {
                // HUD 那组按钮可能先藏起来了（例如从 HUD 点进设置）：退出 HUD 语义再扫一次，
                // 让新出现的菜单页面接上，而不是当场交还键盘、下一轮再自动接管。
                if (_hudMode)
                {
                    _hudMode = false;
                    Scan();
                }
                if (Groups.Count == 0 || Items.Count == 0) { Disengage(false); return; }
            }

            string nowPage = PageKey();
            if (nowPage != beforePage)
            {
                // 换了页面（打开子菜单、确认框、从 HUD 点进设置……）：接着接管新页面
                BlockPageGameInput(false);
                _pageRoot = Groups[0].Root;
                _pageKey = nowPage;
                // 从对话 HUD 点进设置后就不再是「操作 HUD」了，热键语义跟着页面走
                _hudMode = PageIsHud();
                _pageLabel = PageLabel();
                _index = 0;
                ResetRepeatState();
                BlockPageGameInput(true);
                AnnouncePage(true);
                return;
            }

            _index = Mathf.Clamp(_index, 0, Items.Count - 1);

            // ★ 保留焦点：按对象身份把原来的当前项找回新清单里的位置。
            //   无脑 Clamp 旧下标时，前面插进/删掉一项会让读屏焦点悄悄漂到隔壁，
            //   玩家按回车就点错了。
            if (before != null)
            {
                int found = Items.IndexOf(before);
                if (found >= 0) _index = found;
            }
            Component now = CurrentItem();
            bool settling = Time.realtimeSinceStartup < _settleUntil;
            bool listChanged = !SameList(beforeList, Items);
            if (now != before || listChanged)
            {
                if (settling)
                {
                    // 动画期：只更新内部状态，不播报，等面板稳定
                }
                else if (activationRescan)
                {
                    Announce("界面已更新。");
                }
                else if (now != before)
                {
                    // 定期重扫/补扫挪了焦点：必须重念现在的当前项，不能悄悄漂移。
                    Announce(null);
                }
            }
        }

        /// <summary>
        /// 清掉我们设的选中态：去掉高亮，也不给 uGUI 留提交目标。
        /// 无条件清 —— 本作游戏自己从不使用 EventSystem 选中态（全库无 SetSelectedGameObject），
        /// 但玩家用鼠标点过按钮后 uGUI 会把那个对象记成提交目标；接管结束还原
        /// sendNavigationEvents 之后，按回车/空格可能把它再点一遍（旧版踩过的坑）。
        /// </summary>
        private static void ReleaseSelection()
        {
            _selectedByUs = null;
            try
            {
                EventSystem es = EventSystem.current;
                if (es != null && es.currentSelectedGameObject != null)
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
            Groups.Clear();
            _inputRoles.Clear();   // 实例 ID 会在对象销毁后被复用，每次重扫都重算
            _rescanRequestedFor = null;
            _excludedModalRoot = "";
            _excludedModalOrder = int.MinValue;
            if (!SceneStable()) return;

            try
            {
                bool diag = Plugin.CfgDiag != null && Plugin.CfgDiag.Value;
                // 详细清单（「全部 Selectable」「有控件但没纳入导航」）非常贵：一次几百行，
                // 整场会话能写出 10MB 日志，是玩家反馈「补丁让游戏更卡」的主要来源之一。
                // 现在只在配置打开、并且「距上次 dump 超过 10 秒」或测试模式下才写；
                // 一行统计照旧，方便玩家报告问题时贴日志。
                bool dump = diag && (Plugin.TestMode
                    || Time.realtimeSinceStartup - _lastDumpAt > 10f);
                List<string> excluded = dump ? new List<string>() : null;
                int inactive = 0, noCanvas = 0, inactiveScene = 0, blockedCount = 0, notInteractable = 0, nonMenuCount = 0;
                Scene activeScene = SceneManager.GetActiveScene();

                Selectable[] all = Resources.FindObjectsOfTypeAll<Selectable>();
                List<string> allDump = dump ? new List<string>() : null;
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
                        if (dump && inactiveScene < 20 && s.gameObject.scene.IsValid()
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
                    if (Blocked(s.transform, dump ? excluded : null)) { blockedCount++; NoteExcludedModal(s.transform); continue; }

                    // ★ 非菜单界面（对话 HUD、字幕、跳过提示、载入、过场、QTE……）不自动接管。
                    //   这些界面没有「可选项」，而且回车必须留给游戏推进剧情 ——
                    //   否则玩家一按回车就被 HUD 按钮吃掉，剧情推不动。
                    //   名单见 CfgUiMenuExcludePaths，用路径片段匹配。
                    if (NonMenu(s.transform)) { nonMenuCount++; NoteExcludedModal(s.transform); continue; }

                    // 整屏「点击穿透层 / 推进热区」按钮：能点，但当作导航项毫无意义，
                    // 焦点停在它上面只会让玩家以为界面没东西可点。
                    if (FullscreenButton(s)) { nonMenuCount++; continue; }

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
                    else if (dump && noCanvas < 10)
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

                    // 白名单文字只有那些面板真的在场时才值得扫：全量
                    // FindObjectsOfTypeAll<TMP_Text> 在对话/加载期是纯开销
                    // （回想、笔记本都不在场，扫出来的字一个也用不上）。
                    bool anyWlGroup = false;
                    for (int gi = 0; gi < Groups.Count && !anyWlGroup; gi++)
                    {
                        string rp = Groups[gi].Root != null ? PathOf(Groups[gi].Root) : "";
                        for (int k = 0; k < frags.Length; k++)
                        {
                            string f0 = frags[k].Trim();
                            if (f0.Length > 0 && rp.IndexOf(f0, StringComparison.OrdinalIgnoreCase) >= 0) { anyWlGroup = true; break; }
                        }
                    }

                    if (anyWlGroup)
                    {
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
                            if (NonMenu(tx.transform)) continue;
                            if (HiddenByCanvasGroup(tx.transform)) continue;

                            Group g = GroupOf(tx);
                            if (g != null) { g.Items.Add(tx); addedText++; }
                        }
                        if (diag && addedText > 0 && Plugin.L != null)
                            Plugin.L.LogInfo("[UiNav] 白名单文字纳入导航 " + addedText + " 条（" + wl + "）");
                    }
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
                // 严格筛完如果没剩下东西，不做「自愈兜底」——原因见下面那段注释。
                int removedByVisibility = 0;
                bool strict = Plugin.CfgUiVisibleOnly == null || Plugin.CfgUiVisibleOnly.Value;

                if (strict) FilterByVisibility(true, dump ? excluded : null, ref removedByVisibility);

                // ★ 这里曾经有一个「自愈兜底」：严格控制后一个都不剩时，退回
                //   「只要是激活的 Selectable 就全收」。那是为了修「菜单里什么都读不到」——
                //   而那个症状真正的病因是 IL2CPP 下 `as`/`is` 静默失败（见 As<T>），
                //   已经修好了。兜底留下只会把**隐藏面板全部重新变成可达**：
                //   实测在游戏内触发过一次，最终纳入从 1 个暴涨到 93 个、18 组，
                //   隐藏的存档界面和剧透面板全回来了 —— 正是玩家报告的那些问题。
                //
                //   原则：无障碍层只能让「已经可见、本来就能点」的东西变得可键盘操作，
                //   绝不能扩大可达范围。筛完没剩下东西，就诚实地告诉玩家没东西可导航。
                if (dump && strict && removedByVisibility > 0)
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

                    // 只读篮子里的「非菜单/穿透层」同样不能进导航
                    g.ReadOnly.RemoveAll(x => x == null || NonMenu(x.transform) || FullscreenButton(x));
                    g.Blocked.RemoveAll(x => x == null || NonMenu(x.transform) || FullscreenButton(x));

                    g.Items.AddRange(g.ReadOnly);   // 看得见、游戏标成不可用
                    g.Items.AddRange(g.Blocked);    // 看得见、游戏标成可用但这一帧点不到（如「返回」）
                    readOnlyKept += g.Items.Count;
                }
                if (readOnlyKept > 0)
                    DiagScan("这一屏没有能按的控件，纳入 " + readOnlyKept + " 个（只读的只念不点）");

                int kept = 0;
                for (int i = 0; i < Groups.Count; i++) kept += Groups[i].Items.Count;
                // 统计行只在真的有界面（kept > 0）或本次要写详细 dump 时才打：
                // 对话/加载期每 0.5 秒一行「0 组」没有信息量，只会撑大日志。
                if (diag && Plugin.L != null && (kept > 0 || dump))
                {
                    Plugin.L.LogInfo(string.Format(
                        "[UiNav] Selectable 总数={0} 未激活={1} 不在Canvas下={2} 开发者面板排除={3} 非菜单/穿透层排除={4} 不可用={5} 被可见性筛掉={6} 最终纳入={7}（其中只读 {8}）",
                        all.Length, inactive, noCanvas, blockedCount, nonMenuCount, notInteractable, removedByVisibility, kept, readOnlyKept));
                }

                // 组排序：Canvas 层级高的、兄弟序号靠后的（在更上层）排前面
                Groups.Sort((a, b) =>
                {
                    if (a.CanvasOrder != b.CanvasOrder) return b.CanvasOrder.CompareTo(a.CanvasOrder);
                    return b.SiblingIndex.CompareTo(a.SiblingIndex);
                });
                Groups.RemoveAll(g => g.Items.Count == 0);
                for (int i = 0; i < Groups.Count; i++) SortWithin(Groups[i]);
                SelectPage();   // ★ 页面合并：同一界面根下的多个 Canvas 组合成一页

                if (dump && Plugin.L != null)
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

                if (dump) DumpScan(excluded);
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
        /// 记录「被排除、但仍显示在最上层」的模态界面（存档读档、选项、开发者面板、QTE/加载/电影…）。
        /// 目的不是去操作它，而是**别让键盘操作点穿到它背后的界面**：
        /// 存档面板盖在标题画面上时，回车绝不能变成点 START。
        /// </summary>
        private static void NoteExcludedModal(Transform t)
        {
            try
            {
                string path = PathOf(t);
                if (path.IndexOf("/ModalUI/", StringComparison.OrdinalIgnoreCase) < 0) return;
                string root = UiRootOf(t);
                if (string.IsNullOrEmpty(root)) return;
                Canvas c = t.GetComponentInParent<Canvas>();
                int order = c != null ? c.sortingOrder : 0;
                if (order > _excludedModalOrder)
                {
                    _excludedModalOrder = order;
                    _excludedModalRoot = root;
                }
            }
            catch { }
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
        /// 「能按的」那一篮的可见性过滤：只把**真正看不见**的（画面外 / CanvasGroup 隐藏）剔掉。
        ///
        /// 以前这里还要求「射线打得到」，打不到的收进 Blocked 备用，于是出过两次
        /// 玩家被困死的问题：
        ///   · CG 画廊的返回按钮在 Top 组，射线被画廊格子挡住；
        ///   · TipsUI 等界面的返回按钮被上层覆盖物挡住。
        /// 游戏按钮的 onClick 根本不看射线，我们程序化激活也不看射线 —— 用「点不到」
        /// 当排除理由只会把出口藏起来。实测日志里这类控件都是 active、interactable、
        /// 在画面矩形内，唯一原因就是优先级更高的东西盖在上面。
        /// </summary>
        private static void FilterBlocked(Group g, bool record, List<string> excluded, ref int removed)
        {
            int n = 0;
            g.Items.RemoveAll(s =>
            {
                bool ok;
                // 只做**便宜的**可见性判定：alpha + 画面矩形。
                //   游戏按钮的 onClick 根本不看射线，我们程序化激活也不看射线 —— 用「点不到」
                //   当排除理由只会把出口藏起来（画廊/Tips 返回按钮被困死就是这么来的）。
                //   旧版这里逐项 RaycastAll，一场游戏约 2.9 万次射线 + 5.8 万次分配，
                //   是补丁卡顿最大的一笔单项开销，0.2.1 起不再做。
                // 判定本身抛异常时按「可见」处理 —— 宁可多念一个，也不要整组消失。
                try { ok = !AlphaHidden(s.transform) && OnScreenOnly(s, g); } catch { ok = true; }
                if (ok) return false;

                n++;
                if (record && excluded != null && excluded.Count < 60)
                    excluded.Add("[画面外] " + PathOf(s.transform));
                return true;
            });
            removed += n;
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
                sb.Append("[UiNav] ===== 诊断：共 ").Append(Groups.Count)
                  .Append(" 组（本页 ").Append(PageGroups.Count)
                  .Append(" 组 / ").Append(PageItems.Count).Append(" 项）=====");
                for (int gi = 0; gi < Groups.Count; gi++)
                {
                    Group g = Groups[gi];
                    bool inPage = PageGroups.IndexOf(g) >= 0;
                    sb.Append("\n[组 ").Append(gi + 1).Append(gi == 0 ? " ★最上层" : "")
                      .Append(inPage ? " ●本页" : "")
                      .Append("] ").Append(g.Root != null ? PathOf(g.Root) : "?")
                      .Append("  项数=").Append(g.Items.Count);
                }
                for (int i = 0; i < PageItems.Count && i < 16; i++)
                {
                    Component s = PageItems[i];
                    string desc = "?";
                    try { desc = Describe(s); } catch (Exception e) { desc = "<Describe 抛异常: " + e.Message + ">"; }
                    sb.Append("\n  #").Append(i + 1).Append(' ').Append(desc)
                      .Append("   [").Append(s != null ? RowLeftOf(s) : "null").Append(']')
                      .Append("  ").Append(s != null ? PathOf(s.transform) : "null");
                }
                if (excluded != null && excluded.Count > 0)
                {
                    sb.Append("\n[有控件但没纳入导航 ").Append(excluded.Count).Append(" 条]");
                    for (int i = 0; i < excluded.Count; i++) sb.Append("\n  ").Append(excluded[i]);
                }
                sb.Append("\n[UiNav] ==== 诊断结束 ====");
                Plugin.L.LogInfo(sb.ToString());
                _lastDumpAt = Time.realtimeSinceStartup;
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
            { "ScreenModePanel",     "显示模式" },
            { "MessageSpeedPanel",   "文本显示速度" },
            { "BgmVolumePanel",      "背景音乐音量" },
            { "SfxVolumePanel",      "音效音量" },
            { "VoiceVolumePanel",    "语音音量" },
            { "LocaleLanguagePanel", "界面语言" },
            { "LocalePanel",         "界面语言" },
            { "VoiceLocalePanel",    "角色语音语言" },

            // ---- 标题画面那条没有任何文字的隐藏滑条（游戏自带的隐藏成就）----
            // RE 取证：TitleUI/LogoPanel/Image_Title/Slider 是 SnappableSlider（0～1.5），
            // 唯一监听是 DissolveController → Material.SetFloat("_DissolveValue")，
            // 只影响标题插画的溶解/显隐外观，不切标题、不影响成就与音效。
            { "Image_Title",         "标题插画显隐" },

            // ---- 设置界面里的美术字按钮（「你的设置」/「我的设置」标题上的「你」/「我」）----
            // RE 取证：普通 UnityEngine.UI.Button@SettingsUI/{PlayerView,CharacterView}/TitlePanel/
            // TextPlayer(TextCharacter)，text 就是「你」/「我」；游戏按结局状态用
            // SetUIState 启用/禁用，启用时我们就能导航到，禁用时鼠标也点不动，不是补丁漏扫。
            { "TextPlayer",          "设置视图切换" },
            { "TextCharacter",       "设置视图切换" },

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

        /// <summary>
        /// 滑条当前值的说法。
        /// 0–1 范围念百分比（「60%」）；其它范围念归一化百分比 + 原始值（「50%（0.75 / 1.5）」），
        /// 这样标题画面那条 max=1.5 的隐藏滑条不会念成「2 / 2」这种会误导人的数。
        /// </summary>
        private static string SliderValueText(Slider sl)
        {
            float range = sl.maxValue - sl.minValue;
            if (range <= 0.0001f) return "不可调";

            float pct = Mathf.Clamp01((sl.value - sl.minValue) / range) * 100f;
            if (sl.minValue >= -0.001f && sl.maxValue <= 1.001f)
                return Mathf.RoundToInt(pct) + "%";

            return Mathf.RoundToInt(pct) + "%（" + sl.value.ToString("0.##") + " / "
                + sl.maxValue.ToString("0.##") + "）";
        }

        private static string Describe(Component s)
        {
            var sb = new StringBuilder();

            Toggle t = As<Toggle>(s);
            Slider sl = As<Slider>(s);
            TMP_InputField inf = As<TMP_InputField>(s);

            string label = inf != null ? InputFieldLabel(inf) : TextOf(s);
            sb.Append(label);

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
            else if (sl != null)
            {
                // 念全：当前值 + 范围 + 步长，玩家不用试就能知道按几下能满。
                // 标签本身以「滑条」结尾时（标题画面那条）别再拼一次，避免「滑条，滑条」。
                if (label.EndsWith("滑条", StringComparison.Ordinal)) sb.Append("，");
                else sb.Append("，滑条，");
                sb.Append(SliderValueText(sl));
                float r = sl.maxValue - sl.minValue;
                if (r > 0.0001f)
                {
                    if (sl.minValue >= -0.001f && sl.maxValue <= 1.001f) sb.Append("，共 100%");
                    float st = sl.wholeNumbers ? Mathf.Max(1f, Mathf.Round(r / 20f)) : r / 20f;
                    sb.Append("，每次 ").Append(Mathf.RoundToInt(st / r * 100f)).Append("%");
                }
            }
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

        private static void AnnouncePage(bool withCount, bool interrupt = true)
        {
            if (Groups.Count == 0 || Items.Count == 0) return;
            string prefix = withCount
                ? ((string.IsNullOrEmpty(_pageLabel) ? "当前界面" : _pageLabel) + "，共 " + Items.Count + " 项。")
                : "";

            // 整页都是「只读」时先说清楚，免得玩家以为补丁坏了：
            // 这是游戏自己把这些控件标成不可用的（本作的彩蛋设置界面就是整屏如此）。
            // 注意：纯文本页（回想条目 / 笔记本）本来就没有 Selectable，别误报「都不可用」。
            try
            {
                if (HasSelectableItem() && UsableCountOfPage() == 0)
                    prefix += "这一屏的控件当前都不可用，只能听，不能选。";
            }
            catch { }

            Announce(prefix, interrupt);
        }

        /// <summary>当前组里有没有真正的 uGUI 控件（纯文本页没有）。</summary>
        private static bool HasSelectableItem()
        {
            for (int i = 0; i < Items.Count; i++)
            {
                try { if (As<Selectable>(Items[i]) != null) return true; }
                catch { }
            }
            return false;
        }

        private static void Announce(string prefix, bool interrupt = true)
        {
            if (!_active || Groups.Count == 0 || Items.Count == 0) return;
            _index = Mathf.Clamp(_index, 0, Items.Count - 1);
            Component s = Items[_index];
            if (s == null) { Disengage(false); return; }

            _announcedItem = s;
            SelectByUs(s.gameObject);

            // 光标移到别处 = 放弃刚才那次退出确认
            ClearQuitConfirm();

            Speech.Speak((prefix ?? "") + Describe(s) + "。" + (_index + 1) + " / " + Items.Count, interrupt);
        }

        // ================= 操作 =================

        /// <summary>当前选中项；界面操作未接管或该项已失效时返回 null。</summary>
        private static Component CurrentItem()
        {
            if (!_active) return null;
            if (Groups.Count == 0 || Items.Count == 0) return null;
            int i = Mathf.Clamp(_index, 0, Items.Count - 1);
            Component s = Items[i];
            return s == null ? null : s;   // Unity 伪空：已销毁对象在此拦下
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
            if (item == null) { Disengage(false); return; }

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

            string label = TextOf(item);
            if (string.IsNullOrEmpty(label)) label = "这一项";

            try
            {
                TMP_InputField inf = As<TMP_InputField>(item);
                if (inf != null)
                {
                    // 输入框拿到焦点后我们暂停接管（见 InputFieldFocused），
                    // 所以这里不退出界面操作，直接让玩家打字即可。
                    SelectByUs(inf.gameObject);
                    inf.ActivateInputField();
                    Speech.Speak("已进入输入框，直接打字即可。", true);
                    RequestRescanNextFrame();
                    return;
                }

                Toggle t = As<Toggle>(item);
                if (t != null)
                {
                    t.isOn = !t.isOn;
                    Speech.Speak("「" + label + "」已" + (t.isOn ? "开启" : "关闭") + "。", false);
                    RequestRescanNextFrame();
                    return;
                }

                Slider sl = As<Slider>(item);
                if (sl != null)
                {
                    // 提示改打断：连按回车时永远听到最新状态，不排队。
                    string head = "「" + label + "」";
                    if (!label.EndsWith("滑条", StringComparison.Ordinal)) head += "，滑条";
                    Speech.Speak(head + "，" + SliderValueText(sl)
                        + "。左方向键减小，右方向键增大。", true);
                    return;
                }

                Button b = As<Button>(item);
                if (b != null)
                {
                    // 不立刻播「已激活」：多数按钮会换页面/弹子面板，
                    // 新页面自己会播报；没换页面时才补一句，免得玩家不确定按没按上。
                    _pendingActivationLabel = label;
                    _pendingActivationPage = PageKey();
                    _pendingActivationAt = Time.realtimeSinceStartup + 0.25f;
                    b.onClick.Invoke();
                    // 下一帧重扫：此刻旧场景仍完整存活；
                    // 若该按钮触发场景切换，离真正卸载还有几十帧
                    RequestRescanNextFrame();
                    return;
                }

                ExecuteEvents.Execute(item.gameObject, new BaseEventData(EventSystem.current),
                    ExecuteEvents.submitHandler);
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

        /// <summary>
        /// 调整滑条一步。返回 true = 值真的变了（连发据此判断要不要继续）。
        /// announce=false 时只改值不播报（连发过程中的节流，见 Update）。
        /// </summary>
        private static bool Adjust(float dir, bool announce = true)
        {
            if (Groups.Count == 0 || Items.Count == 0) return false;
            int i = Mathf.Clamp(_index, 0, Items.Count - 1);
            Component item = Items[i];
            Slider sl = As<Slider>(item);
            if (sl == null) return false;   // 只有滑条响应左右键；其它控件不吞键

            try
            {
                var sel = As<Selectable>(item);
                if (sel != null && !sel.interactable)
                {
                    // 彩蛋设置界面 / 载入进度条这种「只念不改」的项，绝不能悄悄改值；
                    // 按住不放也只提醒一次（_repEndSaid），不然每 0.12 秒念一遍很吵。
                    if (announce && !_repEndSaid)
                    {
                        Speech.Speak("该项当前不可用。", true);
                        _repEndSaid = true;
                    }
                    return false;
                }

                float range = sl.maxValue - sl.minValue;
                if (range <= 0.0001f) return false;
                float step = sl.wholeNumbers ? Mathf.Max(1f, Mathf.Round(range / 20f)) : range / 20f;
                float next = Mathf.Clamp(sl.value + dir * step, sl.minValue, sl.maxValue);
                bool changed = Mathf.Abs(next - sl.value) > step * 0.5f;
                if (!changed)
                {
                    if (announce && !_repEndSaid)
                    {
                        string endLabel = TextOf(item);
                        if (string.IsNullOrEmpty(endLabel)) endLabel = "这一项";
                        Speech.Speak(endLabel + "，" + SliderValueText(sl) + "，已到顶。", true);
                        _repEndSaid = true;
                    }
                    return false;
                }
                _repEndSaid = false;
                sl.value = next;   // Unity setter 会触发 onValueChanged，游戏自己会应用音量/速度

                if (announce)
                {
                    // 念「名称 + 新值」并打断上一句：连按时听到的永远是最新值。
                    string label = TextOf(item);
                    if (string.IsNullOrEmpty(label)) label = "这一项";
                    Speech.Speak(label + "，" + SliderValueText(sl) + "。", true);
                }
                return true;
            }
            catch (Exception e) { Plugin.L.LogError("调整滑条失败: " + e.Message); return false; }
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
                    if (_active) Disengage(false);
                    Groups.Clear();
                    PageGroups.Clear();
                    PageItems.Clear();
                    _candidateSince = -1f;
                    _candidateKey = "";
                    _candidateScans = 0;
                    _excludedModalRoot = "";
                    _excludedModalOrder = int.MinValue;
                    _pendingActivationAt = -1f;
                    return;
                }
            }
            catch { }

            // ---- 输入框正在收打字：我们一个键都别碰 ----
            if (InputFieldFocused())
            {
                ClearQuitConfirm();
                return;
            }

            // ---- 选项框出现：键盘完全让给数字键 1–9（Choices 模块），UiNav 不接管 ----
            //   选项面板可能不在 Naninovel 的 /ModalUI 根下（自定义选项界面），
            //   只靠路径排除不可靠；这里用 Choices.Live 做不依赖路径的让行判断。
            if (Choices.Live)
            {
                if (_active) Disengage(false);
                _candidateSince = -1f;
                _candidateKey = "";
                _candidateScans = 0;
                ClearQuitConfirm();
                return;
            }

            // ---- 延迟的「已激活」补播 ----
            TickPendingActivation();

            // ---- 还没接管：HUD 显示重试窗口 / 空闲时侦测有没有界面出现 ----
            if (!_active)
            {
                if (_hudRetryUntil > 0f)
                {
                    HudRetry();
                    if (_active) return;
                }
                IdleDetect();
                return;
            }

            // ---- 已经接管：页面被游戏关掉了（点了关闭、剧情推进、面板隐藏）→ 交还键盘 ----
            if (!PageStillAlive())
            {
                Disengage(false);
                return;
            }

            // ---- 激活后 / 定期重扫 ----
            bool due1 = _pendingRescanFrame >= 0 && Time.frameCount >= _pendingRescanFrame;
            bool due2 = _pendingRescanFrame2 >= 0 && Time.frameCount >= _pendingRescanFrame2;
            if (due1 || due2)
            {
                // 只清到期的那一个：先到的「下一帧重扫」不能把稍后的补扫一起吞掉，
                // 否则滑入/淡入动画期间新出现的控件会漏掉。
                if (due1) _pendingRescanFrame = -1;
                if (due2) _pendingRescanFrame2 = -1;
                RescanEngaged(due1);
                if (!_active) return;
            }
            else if (Time.realtimeSinceStartup >= _nextEngagedScan)
            {
                _nextEngagedScan = Time.realtimeSinceStartup + 1.0f;
                RescanEngaged(false);
                if (!_active) return;
            }

            // ---- 当前项失效 → 节流重扫 ----
            // 选项框（ChoiceHandlerPanel）的按钮是池化复用的：扫描那一刻抓到的那个
            // Button 下一帧就可能被换掉/销毁，于是 CurrentItem() 返回 Unity 伪空、
            // 回车那一支直接 return（按键原样交还游戏），既没反应也没提示。
            if (_pendingRescanFrame < 0 && Time.realtimeSinceStartup >= _nextDeadRescan)
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

            // ---- 回车 / 空格：整块状态机唯一的判定点 ----
            //
            //   没有接管 / 没有可用项  → 什么都不做，游戏自己推进剧情
            //   接管中 + 有可用项      → 我们激活它，并拦住游戏同一帧的推进
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)
                || Input.GetKeyDown(KeyCode.Space))
            {
                Component target = CurrentItem();

                // 当前项已经死掉时当场重扫一次再试：这样第一次按回车就能生效，
                // 而不是要按两下。
                if (target == null)
                {
                    RescanEngaged(false);
                    if (_active) target = CurrentItem();
                }

                // 页面刚好被游戏关掉（Esc / 关闭按钮 / 剧情推进）时，清单还停在上一帧：
                // 这时把回车交还游戏，别去点一个已经藏起来的按钮（点中会重新打开面板）。
                if (target != null && (HiddenByCanvasGroup(target.transform)
                    || !target.gameObject.activeInHierarchy))
                {
                    RescanEngaged(false);
                    target = _active ? CurrentItem() : null;
                }
                if (target != null)
                {
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
                    // 而游戏自己换面板不会通知我们。所以无条件排两次重扫。
                    RequestRescanNextFrame();
                    RequestRescanDelayed();
                }
                return;
            }

            // ---- 退出确认的「按别的键取消」 ----
            // 播报里说「按别的键取消」，就得真的取消；否则 8 秒内按 Esc/F9/退格
            // 都落在这个状态里，玩家以为取消了、其实再按回车就退出游戏了。
            if (_pendingQuit != null && Input.anyKeyDown
                && !Input.GetKeyDown(KeyCode.Return)
                && !Input.GetKeyDown(KeyCode.KeypadEnter)
                && !Input.GetKeyDown(KeyCode.Space))
            {
                ClearQuitConfirm();
            }

            // ---- 浏览 / 调值 ----
            if (Items.Count == 0) return;
            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                ResetRepeatState();
                _index = (_index - 1 + Items.Count) % Items.Count;
                Announce(null);
            }
            else if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                ResetRepeatState();
                _index = (_index + 1) % Items.Count;
                Announce(null);
            }
            else if (Input.GetKeyDown(KeyCode.Home)) { ResetRepeatState(); _index = 0; Announce(null); }
            else if (Input.GetKeyDown(KeyCode.End)) { ResetRepeatState(); _index = Items.Count - 1; Announce(null); }
            else
            {
                // 左右键：当前项是滑条时支持「按住连续调整」（20 档要按 20 次太累）；
                // 其它控件上左右键不响应、也不吞键（普通按钮不该被左右键碰）。
                Slider curSlider = Items.Count > 0
                    ? As<Slider>(Items[Mathf.Clamp(_index, 0, Items.Count - 1)]) : null;
                if (curSlider != null)
                {
                    float now = Time.realtimeSinceStartup;
                    if (Input.GetKeyDown(KeyCode.LeftArrow))
                    {
                        _repActive = true;
                        _repSteps = 0;
                        _repDir = -1;
                        Adjust(-1f, true);
                        _repAt = now + RepeatFirstDelay;
                    }
                    else if (Input.GetKeyDown(KeyCode.RightArrow))
                    {
                        _repActive = true;
                        _repSteps = 0;
                        _repDir = 1;
                        Adjust(1f, true);
                        _repAt = now + RepeatFirstDelay;
                    }
                    else if (_repActive && _repDir != 0
                        && Input.GetKey(_repDir < 0 ? KeyCode.LeftArrow : KeyCode.RightArrow)
                        && now >= _repAt)
                    {
                        // 连发每 3 步才念一次值：0.12 秒一步，全念会把 TTS 打碎成
                        // 「六十、六十五、七十」三连击，听感反而更差。
                        _repSteps++;
                        bool speak = (_repSteps % 3) == 0;
                        if (!Adjust(_repDir, speak)) _repDir = 0;   // 到顶/不可用：停下
                        else _repAt = now + RepeatInterval;
                    }

                    // 松手：念一次最终值（连发时最后一个值可能没念），然后复位。
                    if (_repActive
                        && !Input.GetKey(KeyCode.LeftArrow) && !Input.GetKey(KeyCode.RightArrow))
                    {
                        SpeakSliderNow();
                        ResetRepeatState();
                    }
                }
                else
                {
                    ResetRepeatState();
                    if (Input.GetKeyDown(KeyCode.LeftArrow)) Adjust(-1f);
                    else if (Input.GetKeyDown(KeyCode.RightArrow)) Adjust(1f);
                }
            }
        }
    }
}
