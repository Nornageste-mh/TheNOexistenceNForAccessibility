using System;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Naninovel;

namespace NoExistenceA11yProbe
{
    /// <summary>
    /// 探针插件 v2：证明"能在运行时拿到文本"。
    ///   A. BasePlugin.AddComponent（BepInEx 会负责 RegisterTypeInIl2Cpp）→ 轮询引擎就绪 → 订阅官方打印事件
    ///   B. Harmony 补丁 Naninovel.UI.RevealableText.set_Text（原生 setter）
    ///   C. 诊断钩子 TMPro.TextMeshProUGUI.set_text —— 必定触发，用来证明原生挂钩链路本身是通的
    /// </summary>
    [BepInPlugin(Guid, "NoExistence A11y Probe", "0.2.0")]
    public class ProbePlugin : BasePlugin
    {
        public const string Guid = "noexistence.a11y.probe";

        internal static ManualLogSource L;
        internal static string OutPath;
        internal static int Lines;
        internal static int TmpCalls;

        public override void Load()
        {
            L = Log;
            OutPath = Path.Combine(Paths.BepInExRootPath, "probe_text.log");
            try
            {
                File.WriteAllText(OutPath,
                    "=== probe v2 start " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===\n",
                    new UTF8Encoding(false));
            }
            catch { }

            Log.LogInfo("[probe] Load() entered");

            try
            {
                var h = new Harmony(Guid);
                h.PatchAll(typeof(RevealableTextPatch));
                Log.LogInfo("[probe] B) patched RevealableText.set_Text");
            }
            catch (Exception e) { Log.LogError("[probe] B) failed: " + e.Message); }

            try
            {
                var h = new Harmony(Guid + ".tmp");
                h.PatchAll(typeof(TmpTextPatch));
                Log.LogInfo("[probe] C) patched TMPro.TextMeshProUGUI.set_text");
            }
            catch (Exception e) { Log.LogError("[probe] C) failed: " + e.Message); }

            try
            {
                AddComponent<ProbeBehaviour>();
                Log.LogInfo("[probe] A) watcher component added");
            }
            catch (Exception e) { Log.LogError("[probe] A) AddComponent failed: " + e); }
        }

        internal static void Emit(string src, string author, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            Lines++;
            try
            {
                File.AppendAllText(OutPath,
                    string.Format("{0}\t{1}\t{2}\n", src, author ?? "", text.Replace("\n", "\\n")),
                    new UTF8Encoding(false));
            }
            catch { }
            if (Lines <= 300) L.LogInfo("[probe] " + src + " | " + (author ?? "") + " | " + text);
        }
    }

    public class ProbeBehaviour : UnityEngine.MonoBehaviour
    {
        private bool _done;
        private int _ticks;

        public void Update()
        {
            _ticks++;
            if (_ticks == 1) ProbePlugin.L.LogInfo("[probe] A) Update() alive");
            TryStartGame();
            if (_done) return;
            try
            {
                if (!Naninovel.Engine.Initialized) return;
                if (_ticks % 60 == 0)
                    ProbePlugin.L.LogInfo("[probe] A) engine initialized, tick=" + _ticks);

                var mgr = Naninovel.Engine.GetService<ITextPrinterManager>();
                if (mgr == null) return;

                Action<PrintTextArgs> handler = OnPrint;
                var del = Il2CppInterop.Runtime.DelegateSupport
                    .ConvertDelegate<Il2CppSystem.Action<PrintTextArgs>>(handler);
                mgr.add_OnPrintTextStarted(del);

                _done = true;
                ProbePlugin.L.LogInfo("[probe] A) SUBSCRIBED to OnPrintTextStarted");
                File.AppendAllText(ProbePlugin.OutPath, "[subscribed]\n", new UTF8Encoding(false));

                _subAt = _ticks;
            }
            catch (Exception e)
            {
                if (_ticks % 300 == 0)
                    ProbePlugin.L.LogWarning("[probe] A) waiting: " + e.GetType().Name + " " + e.Message);
            }
        }

        private int _subAt;
        private bool _clicked;

        /// <summary>订阅成功后等 5 秒仍无文本，就自己把标题界面的 START 按钮按下去。</summary>
        private void TryStartGame()
        {
            if (_clicked || _subAt == 0 || _ticks - _subAt < 300) return;
            _clicked = true;
            try
            {
                string[] names = { "CustomEnterGameButton", "TitleNewGameButton", "StartGame" };
                foreach (var n in names)
                {
                    var go = UnityEngine.GameObject.Find(n);
                    ProbePlugin.L.LogInfo("[probe] Find(\"" + n + "\") -> " + (go != null));
                    if (go == null) continue;
                    var btn = go.GetComponent<UnityEngine.UI.Button>();
                    if (btn == null) { ProbePlugin.L.LogInfo("[probe]   no Button component"); continue; }
                    btn.onClick.Invoke();
                    ProbePlugin.L.LogInfo("[probe]   >>> INVOKED onClick on " + n);
                    File.AppendAllText(ProbePlugin.OutPath, "[clicked " + n + "]\n", new UTF8Encoding(false));
                    return;
                }
                // 兜底：列出场景里所有按钮对象名，便于下一轮定位
                ProbePlugin.L.LogInfo("[probe] no known start button found; dumping candidates");
                var all = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.UI.Button>();
                int n2 = 0;
                foreach (var b in all)
                {
                    if (b == null || b.gameObject == null) continue;
                    ProbePlugin.L.LogInfo("[probe]   button: " + b.gameObject.name);
                    if (++n2 >= 40) break;
                }
            }
            catch (Exception e)
            {
                ProbePlugin.L.LogError("[probe] TryStartGame failed: " + e);
            }
        }

        private void OnPrint(PrintTextArgs args)
        {
            try
            {
                string text = args.Text == null ? null : args.Text.ToString();
                ProbePlugin.Emit("EVENT", args.AuthorId, text);
            }
            catch (Exception e) { ProbePlugin.L.LogError("[probe] OnPrint: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(Naninovel.UI.RevealableText), "set_Text")]
    public static class RevealableTextPatch
    {
        [HarmonyPostfix]
        public static void Postfix(string value) => ProbePlugin.Emit("REVEAL", "", value);
    }

    [HarmonyPatch(typeof(TMPro.TextMeshProUGUI), "set_text")]
    public static class TmpTextPatch
    {
        [HarmonyPostfix]
        public static void Postfix(string value)
        {
            ProbePlugin.TmpCalls++;
            if (ProbePlugin.TmpCalls == 1)
                ProbePlugin.L.LogInfo("[probe] C) FIRST TMP set_text fired -> 原生挂钩链路可用");
            if (ProbePlugin.TmpCalls <= 400) ProbePlugin.Emit("TMP", "", value);
        }
    }
}
