using System.Text;
using System.Text.RegularExpressions;

namespace NoExistenceA11y
{
    /// <summary>
    /// 文本处理：把 Naninovel 的原始富文本变成适合朗读的字符串。
    ///
    /// 关键决策（见 语音层设计.md）：
    ///   · &lt;ruby="注"&gt;正文&lt;/ruby&gt; —— 本作是「正文=表象、旁注=真相」，两个都要读，
    ///     且必须显式标注旁注。只读正文会让盲人玩家错过题眼。
    ///   · █ 是游戏里的「被抹除」符号，逐字念会变成噪音，统一念作「方块」。
    ///   · 源文里 ";;" 之后是给译者看的行内注释（如 ";; @wait 2"），不朗读。
    /// </summary>
    internal static class TextProc
    {
        private static readonly Regex RubyRe =
            new Regex("<ruby=\"([^\"]*)\">(.*?)</ruby>", RegexOptions.Singleline);

        private static readonly Regex TagRe =
            new Regex("<[^<>]{1,80}>", RegexOptions.Singleline);

        private static readonly Regex BlockRe =
            new Regex("[█■□]+", RegexOptions.Singleline);

        private static readonly Regex CommentRe =
            new Regex(@";;\s[^\r\n]*", RegexOptions.Singleline);

        private static readonly Regex RefRe =
            new Regex(@"\|#([^|]+)\|", RegexOptions.Singleline);

        /// <summary>把 "|#Prologue2_1/~2f1a8e2c|" 解成 "Prologue2_1/~2f1a8e2c"；解不出返回 null。</summary>
        public static string ExtractLineKey(string reference)
        {
            if (string.IsNullOrEmpty(reference)) return null;
            var m = RefRe.Match(reference);
            if (m.Success) return m.Groups[1].Value;
            // 有些路径是 "Script/lineId" 直接给出来的
            if (reference.IndexOf('/') > 0 && reference.IndexOf('|') < 0
                && reference.IndexOf(' ') < 0 && reference.Length < 64)
                return reference;
            return null;
        }

        /// <summary>原始富文本 → 朗读文本。</summary>
        public static string ToSpeech(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";

            string s = raw;

            // 1. ruby：正文（实为：旁注）
            s = RubyRe.Replace(s, m =>
            {
                string ann = m.Groups[1].Value;
                string bas = m.Groups[2].Value;
                if (string.IsNullOrEmpty(ann)) return bas;
                if (ann == bas) return bas;
                return bas + "（实为：" + ann + "）";
            });

            // 2. 行内注释（; @wait 2 之类）不朗读
            s = CommentRe.Replace(s, "");

            // 3. 其余富文本标签
            s = TagRe.Replace(s, "");

            // 4. 换行 / 分页标记 → 停顿
            s = s.Replace("\r\n", "\n").Replace("\r", "\n");
            s = s.Replace("/pg", "\n");
            s = s.Replace("\n", "，");

            // 5. 抹除符号
            s = BlockRe.Replace(s, "方块");

            // 6. 收尾：合并重复标点与空白
            s = Regex.Replace(s, "[ \t\u3000]+", " ");
            s = Regex.Replace(s, "，{2,}", "，");
            s = Regex.Replace(s, "。{2,}", "。");
            s = s.Trim(' ', '，', '\n');

            return s;
        }

        /// <summary>去重用的指纹：忽略空白与标点，只看字。</summary>
        public static string Fingerprint(string speech)
        {
            if (string.IsNullOrEmpty(speech)) return "";
            var sb = new StringBuilder(speech.Length);
            foreach (char c in speech)
            {
                if (char.IsWhiteSpace(c)) continue;
                if (char.IsPunctuation(c)) continue;
                if (char.IsSymbol(c)) continue;
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>这句话是否只是省略号/标点（不值得单独朗读）。</summary>
        public static bool IsTrivial(string speech)
        {
            if (string.IsNullOrEmpty(speech)) return true;
            foreach (char c in speech)
            {
                if (char.IsLetterOrDigit(c)) return false;
                if (c >= 0x4E00 && c <= 0x9FFF) return false;
            }
            return true;
        }
    }
}
