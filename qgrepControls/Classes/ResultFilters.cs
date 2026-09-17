using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace qgrepControls.Classes
{
    /// <summary>
    /// CK1-CK10 自定义复选框对应的筛选位，位序与 qgrepSearchWindow 里的控件顺序一致（bit0 = CK1）。
    /// </summary>
    public static class CustomFlag
    {
        /// <summary>CK1「含注释」：勾选时保留注释里的匹配；未勾选时按文件扩展名对应的注释语法隐藏注释里的匹配。</summary>
        public const int IncludeComments = 1 << 0;

        /// <summary>CK2「只看当前文件」：只显示当前编辑器活动文件里找到的匹配。</summary>
        public const int CurrentFileOnly = 1 << 1;

        /// <summary>CK3「排除DSN」：不显示 .designer.cs / .designer.vb 里找到的匹配。</summary>
        public const int ExcludeDesigner = 1 << 2;
    }

    /// <summary>路径比较工具：qgrep 输出的路径与 IDE 给出的路径可能在分隔符、大小写上不一致。</summary>
    public static class ResultPath
    {
        public static string NormalizeForCompare(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "";
            }

            string result = path.Trim().Replace('\\', '/');

            while (result.IndexOf("//", StringComparison.Ordinal) >= 0)
            {
                result = result.Replace("//", "/");
            }

            return result.TrimEnd('/').ToLowerInvariant();
        }

        public static string GetFileName(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "";
            }

            int separator = path.LastIndexOfAny(new[] { '/', '\\' });
            return separator >= 0 ? path.Substring(separator + 1) : path;
        }

        /// <summary>判断两个路径是否指向同一个文件（允许一边是相对路径、另一边是完整路径）。</summary>
        public static bool IsSameFile(string left, string right)
        {
            string normalizedLeft = NormalizeForCompare(left);
            string normalizedRight = NormalizeForCompare(right);

            if (normalizedLeft.Length == 0 || normalizedRight.Length == 0)
            {
                return false;
            }

            if (string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal))
            {
                return true;
            }

            return normalizedLeft.EndsWith("/" + normalizedRight, StringComparison.Ordinal) ||
                normalizedRight.EndsWith("/" + normalizedLeft, StringComparison.Ordinal);
        }
    }

    /// <summary>VS 设计器自动生成的代码文件（CK3「排除DSN」使用）。</summary>
    public static class DesignerFiles
    {
        private static readonly string[] Suffixes = new[] { ".designer.cs", ".designer.vb" };

        public static bool IsDesignerFile(string path)
        {
            string fileName = ResultPath.GetFileName(path);

            if (fileName.Length == 0)
            {
                return false;
            }

            foreach (string suffix in Suffixes)
            {
                if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>行注释规则，如 "//"、"#"、"REM"。</summary>
    internal sealed class LineCommentRule
    {
        /// <summary>注释标记本身。</summary>
        public readonly string Marker;

        /// <summary>标记必须位于行首（前面只允许空白，批处理允许一个 @ 前缀）。</summary>
        public readonly bool LineStartOnly;

        /// <summary>标记前一个字符属于此集合时不算注释（用于排除 http:// 这类 URL）。</summary>
        public readonly string NotAfter;

        /// <summary>行首是否允许一个 "@" 前缀（批处理的 @REM / @::）。</summary>
        public readonly bool AllowAtPrefix;

        /// <summary>标记全由字母组成时（如 REM），其后必须跟分隔符，避免把 REMOVE 当成注释。</summary>
        public readonly bool WordMarker;

        public LineCommentRule(string marker, bool lineStartOnly, string notAfter, bool allowAtPrefix)
        {
            Marker = marker;
            LineStartOnly = lineStartOnly;
            NotAfter = notAfter;
            AllowAtPrefix = allowAtPrefix;

            bool lettersOnly = !string.IsNullOrEmpty(marker);
            if (lettersOnly)
            {
                foreach (char c in marker)
                {
                    if (!char.IsLetter(c))
                    {
                        lettersOnly = false;
                        break;
                    }
                }
            }

            WordMarker = lettersOnly;
        }
    }

    /// <summary>块注释规则，如 /* */ 、&lt;!-- --&gt; 、--[[ ]] 。</summary>
    internal sealed class BlockCommentRule
    {
        public readonly string Begin;
        public readonly string End;

        public BlockCommentRule(string begin, string end)
        {
            Begin = begin;
            End = end;
        }
    }

    /// <summary>字符串字面量规则：字符串里的注释标记不算注释。</summary>
    internal sealed class StringRule
    {
        public readonly string Open;
        public readonly string Close;

        /// <summary>是否支持反斜杠转义。</summary>
        public readonly bool Escapes;

        /// <summary>是否允许跨行（模板字符串、三引号、C# 逐字字符串等）。</summary>
        public readonly bool MultiLine;

        public StringRule(string open, string close, bool escapes, bool multiLine)
        {
            Open = open;
            Close = close;
            Escapes = escapes;
            MultiLine = multiLine;
        }

        public static readonly StringRule DoubleQuoteEscaped = new StringRule("\"", "\"", true, false);
        public static readonly StringRule SingleQuoteEscaped = new StringRule("'", "'", true, false);
        public static readonly StringRule DoubleQuotePlain = new StringRule("\"", "\"", false, false);
        public static readonly StringRule SingleQuotePlain = new StringRule("'", "'", false, false);
        public static readonly StringRule Backtick = new StringRule("`", "`", true, true);
        public static readonly StringRule Verbatim = new StringRule("@\"", "\"", false, true);
        public static readonly StringRule TripleDouble = new StringRule("\"\"\"", "\"\"\"", true, true);
        public static readonly StringRule TripleSingle = new StringRule("'''", "'''", true, true);
        public static readonly StringRule LuaLong = new StringRule("[[", "]]", false, true);
    }

    /// <summary>
    /// 嵌入区域：HTML 里的 &lt;script&gt;/&lt;style&gt;、ASP.NET 里的 &lt;% %&gt; 等，
    /// 区域内改用另一种注释语法。
    /// </summary>
    internal sealed class EmbeddedRegion
    {
        public readonly string[] Begins;
        public readonly string[] Ends;

        /// <summary>结束标记必须位于行首（用于 @{ } 这类按行闭合的代码块）。</summary>
        public readonly bool EndAtLineStart;

        public readonly CommentSyntax Syntax;

        public EmbeddedRegion(string[] begins, string[] ends, bool endAtLineStart, CommentSyntax syntax)
        {
            Begins = begins;
            Ends = ends;
            EndAtLineStart = endAtLineStart;
            Syntax = syntax;
        }
    }

    /// <summary>按语言组合出来的注释语法。所有规则都是「或」关系，具体哪一种生效由扫描顺序决定。</summary>
    internal sealed class CommentSyntax
    {
        private static readonly LineCommentRule SlashSlash = new LineCommentRule("//", false, null, false);
        // HTML 里的 // 常见于 URL（https://），所以前一个字符是冒号时不算注释
        private static readonly LineCommentRule SlashSlashNotUrl = new LineCommentRule("//", false, ":", false);
        private static readonly LineCommentRule HashLine = new LineCommentRule("#", false, null, false);
        private static readonly LineCommentRule DashDash = new LineCommentRule("--", false, null, false);
        private static readonly LineCommentRule Semicolon = new LineCommentRule(";", false, null, false);
        private static readonly LineCommentRule Apostrophe = new LineCommentRule("'", false, null, false);
        private static readonly LineCommentRule Percent = new LineCommentRule("%", false, null, false);
        private static readonly LineCommentRule Bang = new LineCommentRule("!", false, null, false);
        private static readonly LineCommentRule RemLine = new LineCommentRule("REM", true, null, true);
        private static readonly LineCommentRule BatchColonColon = new LineCommentRule("::", true, null, false);
        private static readonly LineCommentRule MarkdownLinkStyle = new LineCommentRule("[//]:", true, null, false);
        private static readonly LineCommentRule MarkdownCommentStyle = new LineCommentRule("[comment]:", true, null, false);

        private static readonly BlockCommentRule CBlock = new BlockCommentRule("/*", "*/");
        private static readonly BlockCommentRule XmlBlock = new BlockCommentRule("<!--", "-->");
        private static readonly BlockCommentRule RazorBlock = new BlockCommentRule("@*", "*@");
        private static readonly BlockCommentRule AspNetBlock = new BlockCommentRule("<%--", "--%>");
        private static readonly BlockCommentRule HaskellBlock = new BlockCommentRule("{-", "-}");
        private static readonly BlockCommentRule PowerShellBlock = new BlockCommentRule("<#", "#>");
        private static readonly BlockCommentRule LuaLongBlock = new BlockCommentRule("--[==[", "]==]");
        private static readonly BlockCommentRule LuaBlock = new BlockCommentRule("--[[", "]]");

        private static readonly StringRule[] QuotesEscaped = new[] { StringRule.DoubleQuoteEscaped, StringRule.SingleQuoteEscaped };
        private static readonly StringRule[] QuotesPlain = new[] { StringRule.DoubleQuotePlain, StringRule.SingleQuotePlain };

        /// <summary>C 系：// 与 /* */。</summary>
        private static readonly CommentSyntax Cpp = new CommentSyntax(new[] { SlashSlash }, new[] { CBlock }, QuotesEscaped, null);

        /// <summary>C#：额外支持 @"..." 逐字字符串（可跨行、不反斜杠转义）。</summary>
        private static readonly CommentSyntax CSharp = new CommentSyntax(
            new[] { SlashSlash },
            new[] { CBlock },
            new[] { StringRule.Verbatim, StringRule.DoubleQuoteEscaped, StringRule.SingleQuoteEscaped },
            null);

        /// <summary>JS/TS：额外支持 ` 模板字符串（可跨行）。</summary>
        private static readonly CommentSyntax Js = new CommentSyntax(
            new[] { SlashSlash },
            new[] { CBlock },
            new[] { StringRule.DoubleQuoteEscaped, StringRule.SingleQuoteEscaped, StringRule.Backtick },
            null);

        /// <summary>纯 CSS：只有 /* */。</summary>
        private static readonly CommentSyntax Css = new CommentSyntax(null, new[] { CBlock }, QuotesEscaped, null);

        /// <summary>SQL / Lua / Ada 一类：-- 与 /* */。</summary>
        private static readonly CommentSyntax Dash = new CommentSyntax(new[] { DashDash }, new[] { CBlock }, QuotesEscaped, null);

        /// <summary>Lua：-- 、--[[ ]]、--[==[ ]==] 与 [[ ]] 长字符串。</summary>
        private static readonly CommentSyntax Lua = new CommentSyntax(
            new[] { DashDash },
            new[] { LuaLongBlock, LuaBlock },
            new[] { StringRule.DoubleQuoteEscaped, StringRule.SingleQuoteEscaped, StringRule.LuaLong },
            null);

        /// <summary>纯 XML：只有 &lt;!-- --&gt;。</summary>
        private static readonly CommentSyntax Xml = new CommentSyntax(null, new[] { XmlBlock }, null, null);

        // ---- HTML 家族：<!-- --> + // + 引号，并识别 <script>/<style> 内部语法 ----
        private static readonly EmbeddedRegion ScriptRegion = new EmbeddedRegion(new[] { "<script" }, new[] { "</script" }, false, Js);
        private static readonly EmbeddedRegion StyleRegion = new EmbeddedRegion(new[] { "<style" }, new[] { "</style" }, false, Css);

        /// <summary>HTML / Vue / Svelte 等：<!-- -->、//（排除 URL）、&lt;script&gt; 内按 JS、&lt;style&gt; 内按 CSS。</summary>
        private static readonly CommentSyntax Html = new CommentSyntax(
            new[] { SlashSlashNotUrl },
            new[] { XmlBlock },
            QuotesPlain,
            new[] { ScriptRegion, StyleRegion });

        /// <summary>Razor（.cshtml/.vbhtml/.razor）：在此之上增加 @* *@ 注释。</summary>
        private static readonly CommentSyntax Razor = new CommentSyntax(
            new[] { SlashSlashNotUrl },
            new[] { RazorBlock, XmlBlock },
            QuotesPlain,
            new[] { ScriptRegion, StyleRegion });

        /// <summary>ASP.NET WebForms：&lt;%-- --%&gt; 服务端注释 + &lt;% %&gt; 代码块。</summary>
        private static readonly CommentSyntax Aspx = new CommentSyntax(
            new[] { SlashSlashNotUrl },
            new[] { AspNetBlock, XmlBlock },
            QuotesPlain,
            new[] { ScriptRegion, StyleRegion });

        /// <summary>Markdown：&lt;!-- --&gt; 与引用式 [//]: / [comment]: 行注释。</summary>
        private static readonly CommentSyntax Markdown = new CommentSyntax(
            new[] { MarkdownLinkStyle, MarkdownCommentStyle },
            new[] { XmlBlock },
            null,
            null);

        /// <summary>VB 系：' 行注释，只有 " 是字符串。</summary>
        private static readonly CommentSyntax Vb = new CommentSyntax(new[] { Apostrophe }, null, new[] { StringRule.DoubleQuotePlain }, null);

        /// <summary>井号系：脚本 / 配置。</summary>
        private static readonly CommentSyntax Hash = new CommentSyntax(new[] { HashLine }, null, QuotesEscaped, null);

        /// <summary>Python：额外支持三引号（docstring 常跨行）。</summary>
        private static readonly CommentSyntax Python = new CommentSyntax(
            new[] { HashLine },
            null,
            new[] { StringRule.TripleDouble, StringRule.TripleSingle, StringRule.DoubleQuoteEscaped, StringRule.SingleQuoteEscaped },
            null);

        /// <summary>PowerShell：# 与 &lt;# #&gt;。</summary>
        private static readonly CommentSyntax PowerShell = new CommentSyntax(new[] { HashLine }, new[] { PowerShellBlock }, QuotesPlain, null);

        /// <summary>ini / cfg / properties 等：; 与 #。</summary>
        private static readonly CommentSyntax Ini = new CommentSyntax(new[] { Semicolon, HashLine }, null, QuotesPlain, null);

        /// <summary>汇编：; 行注释。</summary>
        private static readonly CommentSyntax Asm = new CommentSyntax(new[] { Semicolon }, null, new[] { StringRule.DoubleQuotePlain, StringRule.SingleQuotePlain }, null);

        /// <summary>批处理：:: 与 REM（都需在行首，允许 @ 前缀）。</summary>
        private static readonly CommentSyntax Batch = new CommentSyntax(new[] { BatchColonColon, RemLine }, null, new[] { StringRule.DoubleQuotePlain }, null);

        /// <summary>Haskell：-- 与 {- -}。</summary>
        private static readonly CommentSyntax Haskell = new CommentSyntax(new[] { DashDash }, new[] { HaskellBlock }, new[] { StringRule.DoubleQuoteEscaped }, null);

        /// <summary>TeX：% 注释。</summary>
        private static readonly CommentSyntax Tex = new CommentSyntax(new[] { Percent }, null, null, null);

        /// <summary>Fortran：! 注释。</summary>
        private static readonly CommentSyntax Fortran = new CommentSyntax(new[] { Bang }, null, QuotesPlain, null);

        public readonly LineCommentRule[] Lines;
        public readonly BlockCommentRule[] Blocks;
        public readonly StringRule[] Strings;
        public readonly EmbeddedRegion[] Regions;

        public CommentSyntax(LineCommentRule[] lines, BlockCommentRule[] blocks, StringRule[] strings, EmbeddedRegion[] regions)
        {
            // 长标记优先，保证 --[[ 不被 -- 抢走、""" 不被 " 抢走、@" 不被 " 抢走
            Lines = SortByLengthDescending(lines, delegate (LineCommentRule rule) { return rule.Marker; });
            Blocks = SortByLengthDescending(blocks, delegate (BlockCommentRule rule) { return rule.Begin; });
            Strings = SortByLengthDescending(strings, delegate (StringRule rule) { return rule.Open; });
            Regions = regions;
        }

        private static T[] SortByLengthDescending<T>(T[] items, Func<T, string> key)
        {
            if (items == null || items.Length == 0)
            {
                return null;
            }

            T[] copy = (T[])items.Clone();
            Array.Sort(copy, delegate (T left, T right)
            {
                return key(right).Length.CompareTo(key(left).Length);
            });

            return copy;
        }

        public bool IsEmpty
        {
            get { return Lines == null && Blocks == null && Strings == null && Regions == null; }
        }

        private static readonly Dictionary<string, CommentSyntax> SyntaxByExtension = BuildSyntaxByExtension();
        private static readonly Dictionary<string, CommentSyntax> SyntaxByFileName = BuildSyntaxByFileName();

        private static void Add(Dictionary<string, CommentSyntax> map, CommentSyntax syntax, params string[] extensions)
        {
            foreach (string extension in extensions)
            {
                map[extension] = syntax;
            }
        }

        private static Dictionary<string, CommentSyntax> BuildSyntaxByExtension()
        {
            Dictionary<string, CommentSyntax> map = new Dictionary<string, CommentSyntax>(StringComparer.OrdinalIgnoreCase);

            // C / C++ / 目标-C / CUDA
            Add(map, Cpp,
                ".c", ".cc", ".cpp", ".cxx", ".c++", ".h", ".hh", ".hpp", ".hxx", ".inl", ".ipp", ".ixx",
                ".m", ".mm", ".cu", ".cuh", ".tli", ".tlh", ".idl", ".acf", ".rc");
            // C# / Java
            Add(map, CSharp, ".cs", ".csx", ".java");
            // JS / TS / 其他 C 系脚本
            Add(map, Js, ".js", ".jsx", ".mjs", ".cjs", ".ts", ".tsx", ".mts", ".cts");
            // 其他 C 系语言
            Add(map, Cpp,
                ".go", ".rs", ".swift", ".kt", ".kts", ".scala", ".dart", ".groovy", ".proto", ".gradle",
                ".php", ".php3", ".php4", ".php5", ".phtml",
                ".glsl", ".hlsl", ".cg", ".fx", ".shader",
                ".less", ".scss", ".sass", ".styl", ".jsonc", ".json5");

            // 纯 CSS（scss/less 已在上面按 Cpp 处理，支持 //）
            Add(map, Css, ".css");

            // 双横线：SQL / Ada
            Add(map, Dash, ".sql", ".pgsql", ".plsql", ".adb", ".ads", ".ada", ".vhdl", ".vhd");
            // Lua 有其专属的长括号语法
            Add(map, Lua, ".lua");

            // 标记语言：<!-- -->。含 Razor 的 .cshtml 单独处理
            Add(map, Xml,
                ".xml", ".xsd", ".xsl", ".xslt", ".dtd", ".resx", ".resw", ".xaml", ".svg", ".plist",
                ".config", ".csproj", ".vbproj", ".vcxproj", ".fsproj", ".props", ".targets", ".manifest",
                ".wxs", ".wxi", ".nuspec", ".vsct", ".vsixmanifest", ".wsdl", ".disco", ".rss", ".atom", ".axml");

            // HTML 家族
            Add(map, Html, ".htm", ".html", ".xhtml", ".shtml", ".vue", ".svelte", ".astro", ".hbs", ".handlebars", ".mustache");
            // Razor
            Add(map, Razor, ".cshtml", ".vbhtml", ".razor");
            // ASP.NET / 模板（<% %> 代码块）
            Add(map, Aspx, ".aspx", ".ascx", ".master", ".ashx", ".asmx", ".asax", ".svc", ".asp", ".ejs", ".erb");

            // Markdown
            Add(map, Markdown, ".md", ".markdown", ".mdx", ".mdown", ".mkd");

            // VB 系
            Add(map, Vb, ".vb", ".vbs", ".vba", ".bas", ".cls", ".frm", ".ctl");

            // 井号：脚本 / 配置
            Add(map, Hash,
                ".sh", ".bash", ".zsh", ".ksh", ".csh", ".fish", ".pl", ".pm", ".rb", ".rake",
                ".yml", ".yaml", ".r", ".toml", ".tcl", ".cmake", ".mk", ".make", ".dockerignore",
                ".gitignore", ".gitattributes", ".gitmodules", ".editorconfig", ".npmrc", ".yarnrc",
                ".env", ".htaccess", ".desktop", ".service", ".socket");
            Add(map, Python, ".py", ".pyw", ".pyi");
            Add(map, PowerShell, ".ps1", ".psm1", ".psd1");

            // ini / cfg / properties / NSIS
            Add(map, Ini, ".ini", ".cfg", ".conf", ".properties", ".nsi", ".nsh", ".inf");
            Add(map, Batch, ".bat", ".cmd", ".btm");
            Add(map, Asm, ".asm", ".s", ".nasm", ".masm");
            Add(map, Haskell, ".hs", ".lhs");
            Add(map, Tex, ".tex", ".sty", ".latex");
            Add(map, Fortran, ".f", ".for", ".f90", ".f95", ".f03", ".f08");

            return map;
        }

        private static Dictionary<string, CommentSyntax> BuildSyntaxByFileName()
        {
            Dictionary<string, CommentSyntax> map = new Dictionary<string, CommentSyntax>(StringComparer.OrdinalIgnoreCase);

            foreach (string name in new[]
            {
                "Makefile", "makefile", "GNUmakefile", "CMakeLists.txt", "Dockerfile", "Containerfile",
                "Jenkinsfile", "Vagrantfile", "Rakefile", "Gemfile", "Procfile", "Brewfile", "Justfile"
            })
            {
                map[name] = Hash;
            }

            return map;
        }

        /// <summary>根据路径判断注释语法；返回 null 表示未知类型，此时不做注释过滤。</summary>
        public static CommentSyntax FromPath(string path)
        {
            string fileName = ResultPath.GetFileName(path);

            if (fileName.Length == 0)
            {
                return null;
            }

            CommentSyntax byName;
            if (SyntaxByFileName.TryGetValue(fileName, out byName))
            {
                return byName;
            }

            int dot = fileName.LastIndexOf('.');
            if (dot < 0 || dot == fileName.Length - 1)
            {
                return null;
            }

            CommentSyntax syntax;
            return SyntaxByExtension.TryGetValue(fileName.Substring(dot), out syntax) ? syntax : null;
        }
    }

    /// <summary>跨行传递的注释扫描状态。</summary>
    internal sealed class CommentState
    {
        /// <summary>当前打开的块注释结束标记。</summary>
        public string BlockEnd;

        /// <summary>当前打开的跨行字符串。</summary>
        public StringRule OpenString;

        /// <summary>当前所处的嵌入区域（如 &lt;script&gt; 内的 JS）。</summary>
        public EmbeddedRegion Region;

        public CommentState Clone()
        {
            return new CommentState { BlockEnd = BlockEnd, OpenString = OpenString, Region = Region };
        }

        public void Clear()
        {
            BlockEnd = null;
            OpenString = null;
            Region = null;
        }
    }

    /// <summary>判断某一行里的某个位置是否落在注释中，同时给出该行结束时的扫描状态。</summary>
    internal static class CommentAnalyzer
    {
        public static bool IsInComment(string line, int matchIndex, CommentState state, CommentSyntax syntax, out CommentState endState)
        {
            endState = state != null ? state.Clone() : new CommentState();

            if (line == null || syntax == null || syntax.IsEmpty)
            {
                endState.Clear();
                return false;
            }

            bool inComment = false;
            int length = line.Length;
            int index = 0;

            // 行首仍处于上一行的块注释中
            if (endState.BlockEnd != null)
            {
                int blockEnd = line.IndexOf(endState.BlockEnd, StringComparison.Ordinal);

                if (blockEnd < 0)
                {
                    // 整行都在注释里；但 HTML 解析器不认注释，区段结束标记仍要生效
                    CloseRegionIfEnded(line, 0, endState);
                    return matchIndex >= 0;
                }

                if (matchIndex >= 0 && matchIndex < blockEnd + endState.BlockEnd.Length)
                {
                    inComment = true;
                }

                index = blockEnd + endState.BlockEnd.Length;
                endState.BlockEnd = null;
            }
            else if (endState.OpenString != null)
            {
                // 行首仍处于上一行的跨行字符串中
                int quoteEnd = line.IndexOf(endState.OpenString.Close, StringComparison.Ordinal);

                if (quoteEnd < 0)
                {
                    CloseRegionIfEnded(line, 0, endState);
                    return false;
                }

                index = quoteEnd + endState.OpenString.Close.Length;
                endState.OpenString = null;
            }

            while (index < length)
            {
                CommentSyntax active = endState.Region != null ? endState.Region.Syntax : syntax;

                // 1) 块注释：放在行注释之前，保证 --[[ 不被 -- 抢走
                BlockCommentRule block = MatchBlockStart(line, index, active);
                if (block != null)
                {
                    int blockEnd = line.IndexOf(block.End, index + block.Begin.Length, StringComparison.Ordinal);
                    int stop = blockEnd < 0 ? length : blockEnd + block.End.Length;

                    if (matchIndex >= index && matchIndex < stop)
                    {
                        inComment = true;
                    }

                    if (blockEnd < 0)
                    {
                        endState.BlockEnd = block.End;
                        CloseRegionIfEnded(line, index, endState);
                        return inComment;
                    }

                    index = stop;
                    continue;
                }

                // 2) 字符串：字符串内部的注释标记不算注释
                StringRule quote = MatchStringStart(line, index, active);
                if (quote != null)
                {
                    int afterQuote = SkipString(line, index, quote);

                    if (afterQuote < 0)
                    {
                        if (quote.MultiLine)
                        {
                            endState.OpenString = quote;
                        }

                        CloseRegionIfEnded(line, index, endState);
                        return inComment;
                    }

                    index = afterQuote;
                    continue;
                }

                // 3) 行注释：之后本行不再有代码
                if (MatchLineComment(line, index, active))
                {
                    if (matchIndex >= index)
                    {
                        inComment = true;
                    }

                    CloseRegionIfEnded(line, index, endState);
                    return inComment;
                }

                // 4) 离开嵌入区域
                if (endState.Region != null)
                {
                    int endLength;
                    if (MatchesRegionEnd(line, index, endState.Region, out endLength))
                    {
                        index += endLength;
                        endState.Region = null;
                        continue;
                    }
                }

                // 5) 进入嵌入区域
                if (endState.Region == null)
                {
                    int beginLength;
                    EmbeddedRegion region = MatchRegionStart(line, index, active, out beginLength);

                    if (region != null)
                    {
                        endState.Region = region;
                        index += beginLength;
                        continue;
                    }
                }

                index++;
            }

            return inComment;
        }

        /// <summary>
        /// HTML 解析器并不理解脚本语言的注释，所以 &lt;script&gt; 里 // 之后的 &lt;/script&gt; 照样闭合区段。
        /// 遇到行注释 / 未闭合的块注释 / 未闭合的跨行字符串时，仍要在本行余下部分找一次区段结束标记。
        /// </summary>
        private static void CloseRegionIfEnded(string line, int fromIndex, CommentState state)
        {
            if (state.Region == null || line == null || fromIndex >= line.Length)
            {
                return;
            }

            foreach (string end in state.Region.Ends)
            {
                if (line.IndexOf(end, fromIndex, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    state.Region = null;
                    return;
                }
            }
        }

        private static BlockCommentRule MatchBlockStart(string line, int index, CommentSyntax syntax)
        {
            if (syntax.Blocks == null)
            {
                return null;
            }

            foreach (BlockCommentRule rule in syntax.Blocks)
            {
                if (MatchAt(line, index, rule.Begin))
                {
                    return rule;
                }
            }

            return null;
        }

        private static StringRule MatchStringStart(string line, int index, CommentSyntax syntax)
        {
            if (syntax.Strings == null)
            {
                return null;
            }

            foreach (StringRule rule in syntax.Strings)
            {
                if (MatchAt(line, index, rule.Open))
                {
                    return rule;
                }
            }

            return null;
        }

        /// <summary>返回字符串结束后的下标；返回 -1 表示本行内没有闭合。</summary>
        private static int SkipString(string line, int index, StringRule rule)
        {
            int position = index + rule.Open.Length;

            while (position < line.Length)
            {
                if (rule.Escapes && line[position] == '\\')
                {
                    position += 2;
                    continue;
                }

                if (MatchAt(line, position, rule.Close))
                {
                    return position + rule.Close.Length;
                }

                position++;
            }

            return -1;
        }

        private static bool MatchLineComment(string line, int index, CommentSyntax syntax)
        {
            if (syntax.Lines == null)
            {
                return false;
            }

            foreach (LineCommentRule rule in syntax.Lines)
            {
                if (!MatchAt(line, index, rule.Marker))
                {
                    continue;
                }

                if (rule.NotAfter != null && index > 0 && rule.NotAfter.IndexOf(line[index - 1]) >= 0)
                {
                    continue;
                }

                if (rule.LineStartOnly && !IsAtLineStart(line, index, rule.AllowAtPrefix))
                {
                    continue;
                }

                if (rule.WordMarker)
                {
                    int after = index + rule.Marker.Length;
                    if (after < line.Length && (char.IsLetterOrDigit(line[after]) || line[after] == '_'))
                    {
                        continue;
                    }
                }

                return true;
            }

            return false;
        }

        private static bool MatchAt(string line, int index, string value)
        {
            if (string.IsNullOrEmpty(value) || index < 0 || index + value.Length > line.Length)
            {
                return false;
            }

            return string.Compare(line, index, value, 0, value.Length, StringComparison.OrdinalIgnoreCase) == 0;
        }

        private static bool IsAtLineStart(string line, int index, bool allowAtPrefix)
        {
            bool atSeen = false;

            for (int i = 0; i < index; i++)
            {
                char current = line[i];

                if (current == ' ' || current == '\t')
                {
                    continue;
                }

                if (allowAtPrefix && !atSeen && current == '@')
                {
                    atSeen = true;
                    continue;
                }

                return false;
            }

            return true;
        }

        private static EmbeddedRegion MatchRegionStart(string line, int index, CommentSyntax syntax, out int beginLength)
        {
            beginLength = 0;

            if (syntax.Regions == null)
            {
                return null;
            }

            foreach (EmbeddedRegion region in syntax.Regions)
            {
                foreach (string begin in region.Begins)
                {
                    if (MatchAt(line, index, begin))
                    {
                        beginLength = begin.Length;
                        return region;
                    }
                }
            }

            return null;
        }

        private static bool MatchesRegionEnd(string line, int index, EmbeddedRegion region, out int endLength)
        {
            endLength = 0;

            if (region.EndAtLineStart && !IsAtLineStart(line, index, false))
            {
                return false;
            }

            foreach (string end in region.Ends)
            {
                if (MatchAt(line, index, end))
                {
                    endLength = end.Length;
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// 按文件增量扫描的注释判断器。qgrep 对同一个文件的结果按行号递增输出，所以只需顺序读一遍文件；
    /// 行号回退或换文件时重新定位。文件句柄用 FileShare.ReadWrite 打开，不会占用用户的文件。
    /// </summary>
    internal sealed class CommentFileScanner : IDisposable
    {
        private string currentPath = null;
        private CommentSyntax currentSyntax = null;
        private StreamReader reader = null;
        private string currentLine = null;
        private int currentLineNumber = 0;
        private CommentState state = new CommentState();

        /// <summary>matchIndex 为匹配在本行（从 0 开始）的偏移，判断它是否在注释里。</summary>
        public bool IsInComment(string path, int lineNumber, int matchIndex)
        {
            if (!EnsureLine(path, lineNumber))
            {
                return false;
            }

            CommentState endState;
            return CommentAnalyzer.IsInComment(currentLine, matchIndex, state, currentSyntax, out endState);
        }

        private bool EnsureLine(string path, int lineNumber)
        {
            if (reader != null && (lineNumber < currentLineNumber || !ResultPath.IsSameFile(path, currentPath)))
            {
                Reset();
            }

            if (reader == null)
            {
                CommentSyntax syntax = CommentSyntax.FromPath(path);

                if (syntax == null || syntax.IsEmpty)
                {
                    return false;
                }

                try
                {
                    FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    reader = new StreamReader(stream, Encoding.UTF8, true);
                }
                catch
                {
                    Reset();
                    return false;
                }

                currentPath = path;
                currentSyntax = syntax;
                currentLine = null;
                currentLineNumber = 0;
                state.Clear();
            }

            while (currentLineNumber < lineNumber)
            {
                string next;

                try
                {
                    next = reader.ReadLine();
                }
                catch
                {
                    Reset();
                    return false;
                }

                if (next == null)
                {
                    // 文件比结果行号短，索引已过期，不做判断
                    Reset();
                    return false;
                }

                if (currentLine != null)
                {
                    CommentState endState;
                    CommentAnalyzer.IsInComment(currentLine, -1, state, currentSyntax, out endState);
                    state = endState;
                }

                currentLine = next;
                currentLineNumber++;
            }

            return currentLine != null;
        }

        private void Reset()
        {
            if (reader != null)
            {
                try
                {
                    reader.Dispose();
                }
                catch
                {
                }
            }

            reader = null;
            currentPath = null;
            currentSyntax = null;
            currentLine = null;
            currentLineNumber = 0;
            state.Clear();
        }

        public void Dispose()
        {
            Reset();
        }
    }

    /// <summary>CK1「含注释」未勾选时用于隐藏注释里的匹配。每次搜索一个实例，搜索结束后释放。</summary>
    public sealed class CommentFilter
    {
        private CommentFileScanner scanner = null;

        /// <summary>未知扩展名 / 无注释语法的文件一律保留，避免误杀。</summary>
        public bool IsMatchInComment(string file, string lineNumber, int matchIndex)
        {
            if (string.IsNullOrEmpty(file) || matchIndex < 0)
            {
                return false;
            }

            int line;
            if (!int.TryParse(lineNumber, out line) || line <= 0)
            {
                return false;
            }

            if (scanner == null)
            {
                scanner = new CommentFileScanner();
            }

            try
            {
                return scanner.IsInComment(file, line, matchIndex);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>释放扫描器持有的文件句柄。</summary>
        public void Reset()
        {
            if (scanner != null)
            {
                scanner.Dispose();
                scanner = null;
            }
        }
    }
}
