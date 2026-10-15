using System;
using UnityEngine;

namespace NoExistenceA11y
{
    // ========================================================================
    // A11yFramework · L1 平台层（**冻结层**，逐作整份复用，不重写）
    //
    // 逐作使用时**只允许改一处**：
    //   namespace —— 由 tools/new_game.py 自动替换，不要手改
    //   本文件只引用 UnityEngine（Input / KeyCode），不引用任何游戏类型、
    //   不引用 A11yHost / BepInEx —— 与其它平台层文件同一条纪律。
    // ========================================================================
    /// <summary>
    /// 按键描述解析 + 边沿检测。给「运行中切换语音后端」这类**玩家自己配的**功能键用。
    ///
    /// 为什么不能沿用 `Enum.TryParse&lt;KeyCode&gt;(配置值)` 就完事：
    ///
    ///   1. **它挡不住冲突避让的需求。** 游戏里 F9 被占用时，玩家需要的写法是
    ///      `Alt+Shift+F9`，而单个 KeyCode 表达不了修饰键。
    ///   2. **它对纯数字是静默错误。** `Enum.TryParse("0")` 返回 true，但得到的是
    ///      `KeyCode.None`（枚举值 0），不是 `Alpha0` —— 于是玩家把键设成「0」
    ///      会得到一个永远不触发的功能键，而且一句提示都没有（流水线 G3）。
    ///
    /// 支持的写法（大小写不敏感，`+` 两边可以有空格）：
    ///
    ///     F9                  单键
    ///     Alt+Shift+F9        修饰键 + 主键（冲突避让就用这种）
    ///     Ctrl+Alt+Backspace  多个修饰键
    ///     LeftAlt+F9          LeftAlt / RightAlt / LeftCtrl / RightCtrl / LeftShift / RightShift
    ///                         都按「任意一侧」处理（同一功能键区分左右没有意义，
    ///                         而且区分了反而更容易配错）
    ///     (空)                已停用，永远不触发
    ///
    /// 两条刻意定下的语义：
    ///
    ///   · **没写修饰键时，按住 Alt/Ctrl/Shift 不触发。** 否则配置写 `F9` 的玩家
    ///     按 `Alt+F9`（游戏自己的键）时会被我们抢走。写了 `Alt+F9` 就只认 Alt。
    ///   · 配置写错时返回 false 且给出人话原因（<c>why</c>），由调用方决定要不要写日志；
    ///     这里不写日志，保持本文件无外部依赖、可被独立测试程序直接编译。
    /// </summary>
    internal static class Hotkey
    {
        /// <summary>解析结果。</summary>
        internal struct Spec
        {
            public KeyCode Key;
            public bool Alt;
            public bool Ctrl;
            public bool Shift;
        }

        /// <summary>
        /// 解析按键描述。返回 false 时 <paramref name="why"/> 是人话原因
        /// （空配置=已停用，也返回 false，但原因写「留空」）。
        /// </summary>
        public static bool Parse(string spec, out Spec parsed, out string why)
        {
            parsed = new Spec();
            why = "";

            if (spec == null || spec.Trim().Length == 0) { why = "留空（这个功能键已停用）"; return false; }

            string[] parts = spec.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) { why = "认不出来：" + spec; return false; }

            bool alt = false, ctrl = false, shift = false;
            string keyName = null;
            foreach (string raw in parts)
            {
                string t = raw.Trim();
                if (t.Length == 0) continue;
                string u = t.ToUpperInvariant();

                if (u == "ALT" || u == "LEFTALT" || u == "RIGHTALT") { alt = true; continue; }
                if (u == "CTRL" || u == "CONTROL" || u == "LEFTCTRL" || u == "RIGHTCTRL") { ctrl = true; continue; }
                if (u == "SHIFT" || u == "LEFTSHIFT" || u == "RIGHTSHIFT") { shift = true; continue; }

                if (keyName != null) { why = "有多个主键（" + keyName + " 和 " + t + "）"; return false; }
                keyName = t;
            }

            if (keyName == null) { why = "只写了修饰键，没有主键"; return false; }

            // G3：纯数字一律拒绝。Enum.TryParse("0") 会成功并给出 KeyCode.None，
            // 玩家会得到一个永远不触发的键，且没有任何提示 —— 这类静默失败必须拦在解析层。
            if (IsAllDigits(keyName)) { why = "纯数字不是合法键名（" + keyName + "）；要数字键请写 Alpha" + keyName; return false; }

            KeyCode key;
            if (!Enum.TryParse(keyName, true, out key)) { why = "Unity 不认识这个键名：" + keyName; return false; }
            if (key == KeyCode.None) { why = "这个键名等于 None：" + keyName; return false; }

            // 往返校验：Enum.TryParse 对数字、别名都会「成功」，只有能原样写回去的
            // 才是我们认的规范键名。否则配置文件里会出现看不懂的值却又不报错。
            if (!string.Equals(key.ToString(), keyName, StringComparison.OrdinalIgnoreCase))
            {
                why = "键名不是规范写法：" + keyName + "（Unity 里应写 " + key + "）";
                return false;
            }

            parsed.Key = key;
            parsed.Alt = alt;
            parsed.Ctrl = ctrl;
            parsed.Shift = shift;
            return true;
        }

        /// <summary>
        /// 这一帧是否按下了配置的功能键（边沿触发，配合调用方每帧调用一次）。
        /// 配置认不出来时返回 false —— 静默不触发，但调用方可以拿 <see cref="Parse"/>
        /// 在启动时把原因写进日志，别让玩家对着一个没反应的键猜。
        /// </summary>
        public static bool Down(string spec)
        {
            Spec s;
            string why;
            if (!Parse(spec, out s, out why)) return false;
            return Down(s);
        }

        /// <summary>已解析的描述是否在这一帧刚被按下。</summary>
        public static bool Down(Spec s)
        {
            try
            {
                if (!Input.GetKeyDown(s.Key)) return false;

                bool altHeld = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

                // 写了哪个修饰键就要求哪个；没写的一律要求「没按住」，
                // 免得配置写 F9 的玩家按 Alt+F9 时被我们抢走游戏自己的键。
                if (s.Alt != altHeld) return false;
                if (s.Ctrl != ctrlHeld) return false;
                if (s.Shift != shiftHeld) return false;
                return true;
            }
            catch
            {
                // 输入系统不可用（编辑器/无头）时当作没按，绝不把异常抛进游戏主循环
                return false;
            }
        }

        private static bool IsAllDigits(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] < '0' || s[i] > '9') return false;
            }
            return true;
        }
    }
}
