using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace qgrepControls.Classes
{
    /// <summary>
    /// 「不包含的目录」规则的规格化与识别。
    ///
    /// 背景：.cfg 只有 path / file / include / exclude / group / endgroup 六种指令，
    /// 引擎（qgrep/src/project.cpp）把 include/exclude 当作正则去匹配文件路径，
    /// 且索引扫描时匹配的是「相对路径」（obj/x64/Debug/a.cs），增量更新时匹配的是
    /// 「绝对路径」（c:/proj/obj/x64/Debug/a.cs），监听回调又是相对路径。
    /// 所以目录规则必须同时覆盖两种形态 —— 统一写成 (^|/)(dir1|dir2)/ 即可：
    ///   · 相对路径  obj/x.cs   → ^ 命中
    ///   · 绝对路径  c:/p/obj/x → /obj/ 命中
    ///   · 反斜杠路径（C# 侧 IsFileRelevant 会先归一化）→ 同样命中
    /// 另外 /obj/ 这种带前置分隔符的写法不会误伤 myobj/xxx，也天然避开 /bin/ 误伤 /binary/。
    ///
    /// 这样新增类型完全不需要改动 C++ 引擎，只需生成特定形式的 exclude 规则。
    /// </summary>
    public static class DirectoryRule
    {
        /// <summary>规格化形式：(^|/)(dir1|dir2)/</summary>
        public const string SampleRegex = "(^|/)(obj)/";

        private static readonly Regex NormalizedPattern = new Regex(@"^\(\^\|/\)\(([^()]+)\)/$", RegexOptions.Compiled);

        /// <summary>目录名之间的分隔符（同时接受空白）。</summary>
        private static readonly char[] InputSeparators = new char[] { ';', ',', '|', ' ', '\t', '\r', '\n' };

        /// <summary>
        /// 把用户输入的目录名 / 目录路径片段整理成可直接写入 .cfg 的正则。
        /// 输入已是规格化形式时原样返回；输入为空时返回空串。
        /// </summary>
        public static string BuildRegex(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return "";
            }

            string trimmed = input.Trim();

            if (IsDirectoryRegex(trimmed))
            {
                return trimmed;
            }

            List<string> names = new List<string>();

            foreach (string part in trimmed.Split(InputSeparators))
            {
                string name = NormalizeSeparators(part);

                if (name.Length > 0 && !names.Contains(name))
                {
                    names.Add(name);
                }
            }

            if (names.Count == 0)
            {
                return "";
            }

            StringBuilder builder = new StringBuilder();
            builder.Append("(^|/)(");

            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append('|');
                }

                builder.Append(EscapeName(names[i]));
            }

            builder.Append(")/");
            return builder.ToString();
        }

        /// <summary>判断一个规则是否为本工具生成的「目录」规则。</summary>
        public static bool IsDirectoryRegex(string rule)
        {
            return !string.IsNullOrEmpty(rule) && NormalizedPattern.IsMatch(rule.Trim());
        }

        /// <summary>把目录规则还原成便于编辑的文本（多个目录用 "; " 连接）。</summary>
        public static string ExtractInput(string rule)
        {
            if (string.IsNullOrEmpty(rule))
            {
                return "";
            }

            Match match = NormalizedPattern.Match(rule.Trim());

            if (!match.Success)
            {
                return rule;
            }

            string[] names = match.Groups[1].Value.Split('|');

            for (int i = 0; i < names.Length; i++)
            {
                names[i] = UnescapeName(names[i]);
            }

            return string.Join("; ", names);
        }

        /// <summary>统一分隔符为 '/'，去掉首尾分隔符并合并重复分隔符。</summary>
        private static string NormalizeSeparators(string value)
        {
            string result = value.Replace('\\', '/');

            while (result.Contains("//"))
            {
                result = result.Replace("//", "/");
            }

            return result.Trim('/');
        }

        /// <summary>目录名按字面量处理，只转义正则元字符（保留 '/' 可读性）。</summary>
        private static string EscapeName(string name)
        {
            return Regex.Escape(name).Replace("\\/", "/");
        }

        private static string UnescapeName(string name)
        {
            return Regex.Replace(name, @"\\(.)", "$1");
        }
    }
}
