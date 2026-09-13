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

                Plugin.Diag("跳转到剧本 " + want + "（从开头）");
                Speech.Speak("跳转到 " + want + "。", true);
                sp.PreloadAndPlayAsync(target, 0, 0);
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
