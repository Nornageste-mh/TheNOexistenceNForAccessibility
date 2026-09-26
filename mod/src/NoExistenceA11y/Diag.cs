using System;
using BepInEx.Configuration;
using Naninovel;
using UnityEngine;

namespace NoExistenceA11y
{
    /// <summary>
    /// 诊断工具：游戏内按热键跳到指定剧本。
    ///
    /// 为什么需要：QTE 在第一章末尾，进度到了后面就够不着了，
    /// 没法回头验证功能。改存档 `playbackSpot` 的做法已经证伪 ——
    /// 行数组和命令载荷块对不上（见 commit 0.0.0.2），硬跳会让角色立绘分层错乱。
    ///
    /// 这里的做法**不碰存档**：直接在内存里按名字找到 `Naninovel.Script` 对象，
    /// 交给 `IScriptPlayer.PreloadAndPlayAsync(script, 0, 0)`，从剧本**开头**播。
    /// 从开头播 = 脚本自己的 `@char` / `@modifyCharacter` 装配命令都会执行，
    /// 立绘不会错乱。
    ///
    /// 用法：进入游戏后按热键（默认 F4）跳到「跳转脚本」指定的剧本。
    ///      配合 Naninovel 自带的跳过键（默认按住 Ctrl）可以几秒钟冲到目标位置。
    ///
    /// ⚠️ 这是诊断功能，会打乱正常流程，而且游戏会自动存档。
    ///    用之前先备份 Saves 目录，测完还原。
    /// </summary>
    internal static class Diag
    {
        private static bool _busy;

        /// <summary>
        /// 把剧本的行表写进诊断日志（配置「转储剧本行表」打开时）。
        ///
        /// 这个引擎按**行号**跳转，而"剧本里某一段在第几行"从资源文件里看不出来：
        /// 行表里既有指令行也有文本行，还夹着标签，肉眼数不准。
        /// 有了这张表就能直接查到行号，填进配置「跳转位置」。
        /// 另外两个能省事的接口：Script.GetLineIndexForLabel(标签) 与
        /// Script.GetCommentForLine(行号)（后者给的是这一行的原文）。
        /// </summary>
        private static void DumpLines(Naninovel.Script s)
        {
            if (Plugin.CfgDumpScript == null || !Plugin.CfgDumpScript.Value) return;
            if (s == null) return;

            try
            {
                // ★ 别用 Script.Lines：那是 Il2Cpp 的 IReadOnlyList 代理，
                //   实测 .Count 和 foreach 都点不出来（接口上没有那些成员）。
                //   直接取内部数组 lines（Il2CppReferenceArray<ScriptLine>），
                //   有 Length 也有索引器，行号就是下标。
                var lines = s.lines;
                if (lines == null) { Plugin.Diag("SCRIPT 行表：拿不到 lines 数组"); return; }

                var tm = s.TextMap;
                int total = lines.Length;

                for (int i = 0; i < total; i++)
                {
                    try
                    {
                        var line = lines[i];
                        if (line == null) { Plugin.Diag(string.Format("  [{0,4}] (null)", i)); continue; }

                        // ★ 必须问 IL2CPP 要**原生类名**：Il2CppInterop 的代理对象上
                        //   GetType() 只会报声明类型 —— 行全都是 "ScriptLine"、
                        //   命令全都是 "Command"，等于什么都没说。
                        string kind = NativeClassName(line);
                        if (kind.Length == 0) { try { kind = line.GetType().Name; } catch { } }

                        string extra = "";
                        var cmd = line.TryCast<Naninovel.CommandScriptLine>();
                        if (cmd != null)
                        {
                            var c = cmd.Command;
                            if (c != null)
                            {
                                extra = "@" + NativeClassName(c);
                                if (extra == "@") extra = "@" + c.GetType().Name;
                            }
                        }
                        else
                        {
                            var lab = line.TryCast<Naninovel.LabelScriptLine>();
                            if (lab != null) extra = "#" + lab.LabelText;
                        }

                        string hash = "";
                        try { hash = line.LineHash ?? ""; } catch { }

                        string body = "";
                        try { if (tm != null && hash.Length > 0) body = tm.GetTextOrNull(hash) ?? ""; } catch { }
                        if (body.Length == 0)
                        {
                            try { body = s.GetCommentForLine(i) ?? ""; } catch { }
                        }

                        Plugin.Diag(string.Format("  [{0,4}] {1,-22} {2,-18} {3,-12} {4}",
                            i, kind, extra, hash, ShortLine(body)));
                    }
                    catch (Exception ex)
                    {
                        Plugin.Diag("  [" + i + "] 读取失败: " + ex.GetType().Name);
                    }
                }

                Plugin.Diag("SCRIPT 行表 " + s.Name + " 结束，共 " + total + " 行");
            }
            catch (Exception e)
            {
                Plugin.Diag("SCRIPT 行表转储失败: " + e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>
        /// 取 IL2CPP 对象的**原生类名**（PrintText / CustomSubtitle / GenericTextScriptLine …）。
        /// Il2CppInterop 交给我们的是按**声明类型**造的代理，
        /// 所以 c.GetType().Name 只会得到 Command；真实类型得直接问 IL2CPP。
        /// 取不到就返回空串，调用方自己回退。
        /// </summary>
        private static string NativeClassName(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase o)
        {
            try
            {
                if (o == null) return "";
                IntPtr ptr = Il2CppInterop.Runtime.IL2CPP.Il2CppObjectBaseToPtr(o);
                if (ptr == IntPtr.Zero) return "";
                IntPtr cls = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(ptr);
                if (cls == IntPtr.Zero) return "";
                return Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_name_(cls) ?? "";
            }
            catch { return ""; }
        }

        /// <summary>行表里每行只留 120 字，够认出来是哪一段即可。</summary>
        private static string ShortLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\n", " ").Replace("\r", " ");
            return s.Length <= 120 ? s : s.Substring(0, 120) + "…";
        }

        private static KeyCode Hotkey()
        {
            try
            {
                string s = Plugin.CfgJumpHotkey != null ? Plugin.CfgJumpHotkey.Value : "F4";
                if (string.IsNullOrEmpty(s)) return KeyCode.F4;
                return (KeyCode)Enum.Parse(typeof(KeyCode), s.Trim(), true);
            }
            catch { return KeyCode.F4; }
        }

        internal static void Update()
        {
            if (Plugin.CfgJumpHotkey == null) return;
            if (Plugin.CfgJumpScript == null || string.IsNullOrEmpty(Plugin.CfgJumpScript.Value)) return;
            if (_busy) return;

            bool down;
            try { down = Input.GetKeyDown(Hotkey()); } catch { return; }
            if (!down) return;

            PerformJump();
        }

        /// <summary>
        /// 执行一次跳转（热键与「自动跳转」两条路都走这里）。
        /// </summary>
        internal static void PerformJump()
        {
            if (_busy) return;
            if (Plugin.CfgJumpScript == null || string.IsNullOrEmpty(Plugin.CfgJumpScript.Value)) return;

            _busy = true;
            string want = Plugin.CfgJumpScript.Value.Trim();
            try
            {
                if (!Engine.Initialized) { Speech.Speak("引擎还没就绪。", true); return; }

                Naninovel.Script target = null;
                var all = Resources.FindObjectsOfTypeAll<Naninovel.Script>();
                if (all != null)
                {
                    foreach (var s in all)
                    {
                        if (s == null) continue;
                        if (string.Equals(s.name, want, StringComparison.OrdinalIgnoreCase)) { target = s; break; }
                    }
                }
                if (target == null)
                {
                    Plugin.Diag("跳转失败：找不到剧本 " + want + "（内存里有 " + (all == null ? 0 : all.Length) + " 个 Script）");
                    Speech.Speak("找不到剧本 " + want + "。", true);
                    return;
                }

                var sp = Engine.GetService<IScriptPlayer>();
                if (sp == null) { Speech.Speak("剧本播放器不可用。", true); return; }

                // 起播行号：默认 0（从开头）。>0 会跳过 @char / @modifyCharacter 的
                // 立绘装配命令，立绘可能错乱 —— 只在排查特定段落时用，配置里写了警告。
                int line = 0;
                try { if (Plugin.CfgJumpLine != null) line = Mathf.Max(0, Plugin.CfgJumpLine.Value); }
                catch { line = 0; }

                DumpLines(target);

                Plugin.Diag("跳转到剧本 " + want + "（第 " + line + " 行起）");
                Speech.Speak(line > 0 ? ("跳转到 " + want + " 第 " + line + " 行。") : ("跳转到 " + want + "。"), true);
                if (line > 0)
                    Plugin.L.LogWarning("[a11y] 跳转位置 = " + line
                        + "：跳过立绘装配命令，立绘可能错乱（诊断用，别在正常游玩时用）");

                sp.PreloadAndPlayAsync(target, line, 0);
            }
            catch (Exception e)
            {
                Plugin.Diag("跳转异常: " + e.GetType().Name + ": " + e.Message);
                Speech.Speak("跳转失败。", true);
            }
            finally
            {
                _busy = false;
            }
        }
    }
}
